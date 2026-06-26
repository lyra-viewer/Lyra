#include <cstdint>
#include <cstdio>
#include <cstdlib>
#include <cstring>
#include <mutex>
#include <unordered_set>

#include <jxl/decode.h>
#include <jxl/cms.h>
#include <jxl/color_encoding.h>
#include <jxl/encode.h>
#include <jxl/resizable_parallel_runner.h>

#ifdef _WIN32
#define JXL_NATIVE_API __declspec(dllexport)
#else
#define JXL_NATIVE_API __attribute__((visibility("default")))
#endif

#ifdef __clang__
#define THREAD_LOCAL __thread
#else
#define THREAD_LOCAL thread_local
#endif

static THREAD_LOCAL char last_jxl_error[512] = "";

static std::unordered_set<void *> jxl_allocated_ptrs;
static std::mutex jxl_alloc_mutex;

static void set_error(const char *msg) {
    if (!msg)
        msg = "Unknown error.";
    std::snprintf(last_jxl_error, sizeof(last_jxl_error), "%s", msg);
}

static void clear_error() { last_jxl_error[0] = '\0'; }

extern "C" {

JXL_NATIVE_API const char *get_last_jxl_error() { return last_jxl_error; }

JXL_NATIVE_API void free_jxl_pixels(void *ptr) {
    if (!ptr)
        return;

    std::lock_guard<std::mutex> lock(jxl_alloc_mutex);
    auto it = jxl_allocated_ptrs.find(ptr);
    if (it == jxl_allocated_ptrs.end())
        return;

    jxl_allocated_ptrs.erase(it);
    std::free(ptr);
}

JXL_NATIVE_API bool decode_jxl_from_memory(
    const uint8_t *data, size_t size,
    int *out_width, int *out_height,
    int *out_is_hdr,
    int *out_bits_per_sample,
    int *out_has_alpha,
    int *out_has_animation,
    uint8_t **out_pixels) {
    clear_error();

    if (out_pixels) *out_pixels = nullptr;
    if (out_width) *out_width = 0;
    if (out_height) *out_height = 0;
    if (out_is_hdr) *out_is_hdr = 0;
    if (out_bits_per_sample) *out_bits_per_sample = 0;
    if (out_has_alpha) *out_has_alpha = 0;
    if (out_has_animation) *out_has_animation = 0;

    if (!data || size == 0 || !out_pixels) {
        set_error("Invalid arguments.");
        return false;
    }

    JxlSignature sig = JxlSignatureCheck(data, size);
    if (sig != JXL_SIG_CODESTREAM && sig != JXL_SIG_CONTAINER) {
        set_error("Not a JPEG XL file.");
        return false;
    }

    JxlDecoder *dec = JxlDecoderCreate(nullptr);
    if (!dec) {
        set_error("JxlDecoderCreate failed.");
        return false;
    }

    void *runner = JxlResizableParallelRunnerCreate(nullptr);

    bool ok = false;
    uint8_t *pixels = nullptr;
    JxlPixelFormat format{};
    bool is_hdr = false;
    int width = 0, height = 0;

    do {
        if (JxlDecoderSetCms(dec, *JxlGetDefaultCms()) != JXL_DEC_SUCCESS) {
            set_error("JxlDecoderSetCms failed.");
            break;
        }

        if (runner &&
            JxlDecoderSetParallelRunner(dec, JxlResizableParallelRunner, runner) != JXL_DEC_SUCCESS) {
            set_error("JxlDecoderSetParallelRunner failed.");
            break;
        }

        if (JxlDecoderSubscribeEvents(
                dec, JXL_DEC_BASIC_INFO | JXL_DEC_COLOR_ENCODING | JXL_DEC_FULL_IMAGE) != JXL_DEC_SUCCESS) {
            set_error("JxlDecoderSubscribeEvents failed.");
            break;
        }

        if (JxlDecoderSetInput(dec, data, size) != JXL_DEC_SUCCESS) {
            set_error("JxlDecoderSetInput failed.");
            break;
        }
        JxlDecoderCloseInput(dec);

        JxlBasicInfo info{};
        size_t buffer_size = 0;

        for (;;) {
            JxlDecoderStatus status = JxlDecoderProcessInput(dec);

            if (status == JXL_DEC_ERROR) {
                set_error("Decoder error.");
                break;
            }
            if (status == JXL_DEC_NEED_MORE_INPUT) {
                set_error("Truncated JPEG XL stream.");
                break;
            }

            if (status == JXL_DEC_BASIC_INFO) {
                if (JxlDecoderGetBasicInfo(dec, &info) != JXL_DEC_SUCCESS) {
                    set_error("JxlDecoderGetBasicInfo failed.");
                    break;
                }

                width = (int) info.xsize;
                height = (int) info.ysize;
                if (width <= 0 || height <= 0) {
                    set_error("Invalid dimensions.");
                    break;
                }

                // HDR == floating-point sample type. A 16-bit *integer* image is
                // still SDR and must go through the plain 8-bit (sRGB) path.
                is_hdr = info.exponent_bits_per_sample > 0;

                format.num_channels = 4;
                format.data_type = is_hdr ? JXL_TYPE_FLOAT : JXL_TYPE_UINT8;
                format.endianness = JXL_NATIVE_ENDIAN;
                format.align = 0;

                if (runner) {
                    JxlResizableParallelRunnerSetThreads(
                        runner, JxlResizableParallelRunnerSuggestThreads(info.xsize, info.ysize));
                }

                if (out_width) *out_width = width;
                if (out_height) *out_height = height;
                if (out_is_hdr) *out_is_hdr = is_hdr ? 1 : 0;
                if (out_bits_per_sample) *out_bits_per_sample = (int) info.bits_per_sample;
                if (out_has_alpha) *out_has_alpha = info.alpha_bits > 0 ? 1 : 0;
                if (out_has_animation) *out_has_animation = info.have_animation ? 1 : 0;
                continue;
            }

            if (status == JXL_DEC_COLOR_ENCODING) {
                JxlColorEncoding target{};
                if (is_hdr)
                    JxlColorEncodingSetToLinearSRGB(&target, JXL_FALSE);
                else
                    JxlColorEncodingSetToSRGB(&target, JXL_FALSE);

                if (JxlDecoderSetOutputColorProfile(dec, &target, nullptr, 0) != JXL_DEC_SUCCESS) {
                    set_error("JxlDecoderSetOutputColorProfile failed.");
                    break;
                }
                continue;
            }

            if (status == JXL_DEC_NEED_IMAGE_OUT_BUFFER) {
                if (JxlDecoderImageOutBufferSize(dec, &format, &buffer_size) != JXL_DEC_SUCCESS) {
                    set_error("JxlDecoderImageOutBufferSize failed.");
                    break;
                }

                size_t bytes_per_pixel = (size_t) 4 * (is_hdr ? sizeof(float) : sizeof(uint8_t));
                size_t expected = (size_t) width * (size_t) height * bytes_per_pixel;
                if (buffer_size != expected) {
                    set_error("Unexpected output buffer size.");
                    break;
                }

                pixels = (uint8_t *) std::malloc(buffer_size);
                if (!pixels) {
                    set_error("Out of memory.");
                    break;
                }

                if (JxlDecoderSetImageOutBuffer(dec, &format, pixels, buffer_size) != JXL_DEC_SUCCESS) {
                    set_error("JxlDecoderSetImageOutBuffer failed.");
                    break;
                }
                continue;
            }

            if (status == JXL_DEC_FULL_IMAGE) {
                ok = (pixels != nullptr);
                if (!ok)
                    set_error("No pixel buffer produced.");
                break;
            }

            if (status == JXL_DEC_SUCCESS) {
                ok = (pixels != nullptr);
                if (!ok)
                    set_error("Decode finished without an image.");
                break;
            }

            set_error("Unexpected decoder status.");
            break;
        }
    } while (false);

    if (runner)
        JxlResizableParallelRunnerDestroy(runner);
    JxlDecoderDestroy(dec);

    if (!ok) {
        if (pixels)
            std::free(pixels);
        return false;
    }

    {
        std::lock_guard<std::mutex> lock(jxl_alloc_mutex);
        jxl_allocated_ptrs.insert(pixels);
    }

    *out_pixels = pixels;
    return true;
}

} // extern "C"
