#include "jxl_native.h"
#include "jxl_internal.h"

#include <cstdlib>

#include <jxl/decode.h>
#include <jxl/resizable_parallel_runner.h>

using namespace jxl_internal;

extern "C" {

JXL_NATIVE_API const char *get_last_jxl_error(void) { return last_error(); }

JXL_NATIVE_API void free_jxl_pixels(void *ptr) {
    if (ptr && release_allocation(ptr))
        std::free(ptr);
}

JXL_NATIVE_API bool decode_jxl_from_memory(const uint8_t *data, size_t size, int *out_width, int *out_height,
                                           int *out_is_hdr, int *out_bits_per_sample, int *out_has_alpha,
                                           int *out_has_animation, uint8_t **out_pixels) {
    clear_error();

    if (out_pixels)
        *out_pixels = nullptr;

    if (out_width)
        *out_width = 0;

    if (out_height)
        *out_height = 0;

    if (out_is_hdr)
        *out_is_hdr = 0;

    if (out_bits_per_sample)
        *out_bits_per_sample = 0;

    if (out_has_alpha)
        *out_has_alpha = 0;

    if (out_has_animation)
        *out_has_animation = 0;

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
        if (!configure_decoder(dec, runner))
            break;

        if (JxlDecoderSubscribeEvents(dec, JXL_DEC_BASIC_INFO | JXL_DEC_COLOR_ENCODING | JXL_DEC_FULL_IMAGE) != JXL_DEC_SUCCESS) {
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

            // The input was closed above, so more input can never arrive: the stream is short.
            if (status == JXL_DEC_NEED_MORE_INPUT) {
                set_error("Truncated JPEG XL stream.");
                break;
            }

            if (status == JXL_DEC_BASIC_INFO) {
                if (JxlDecoderGetBasicInfo(dec, &info) != JXL_DEC_SUCCESS) {
                    set_error("JxlDecoderGetBasicInfo failed.");
                    break;
                }

                // Already upright: unless asked to keep the orientation, libjxl applies it and
                // reports the oriented size here, despite the header's "before orientation".
                width = (int) info.xsize;
                height = (int) info.ysize;

                // Also rejects a dimension past INT_MAX, which the cast above turns negative.
                if (width <= 0 || height <= 0) {
                    set_error("Invalid dimensions: %ux%u.", info.xsize, info.ysize);
                    break;
                }

                is_hdr = is_hdr_image(info);
                format = pixel_format(is_hdr);

                if (runner)
                    JxlResizableParallelRunnerSetThreads(runner, JxlResizableParallelRunnerSuggestThreads(info.xsize, info.ysize));

                if (out_width)
                    *out_width = width;

                if (out_height)
                    *out_height = height;

                if (out_is_hdr)
                    *out_is_hdr = is_hdr ? 1 : 0;

                if (out_bits_per_sample)
                    *out_bits_per_sample = (int) info.bits_per_sample;

                if (out_has_alpha)
                    *out_has_alpha = info.alpha_bits > 0 ? 1 : 0;

                if (out_has_animation)
                    *out_has_animation = info.have_animation ? 1 : 0;

                continue;
            }

            if (status == JXL_DEC_COLOR_ENCODING) {
                if (!set_output_profile(dec, is_hdr))
                    break;

                continue;
            }

            if (status == JXL_DEC_NEED_IMAGE_OUT_BUFFER) {
                if (width <= 0 || height <= 0) {
                    set_error("Output buffer requested before basic info.");
                    break;
                }

                if (JxlDecoderImageOutBufferSize(dec, &format, &buffer_size) != JXL_DEC_SUCCESS) {
                    set_error("JxlDecoderImageOutBufferSize failed.");
                    break;
                }

                size_t bytes_per_pixel = (size_t) 4 * (is_hdr ? sizeof(float) : sizeof(uint8_t));
                size_t expected = (size_t) width * (size_t) height * bytes_per_pixel;
                if (buffer_size != expected) {
                    set_error("Unexpected output buffer size: %zu, expected %zu.", buffer_size, expected);
                    break;
                }

                pixels = (uint8_t *) std::malloc(buffer_size);
                if (!pixels) {
                    set_error("Out of memory: %zu bytes for a %dx%d image.", buffer_size, width, height);
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

            set_error("Unexpected decoder status: %d.", (int) status);
            break;
        }
    } while (false);

    if (runner)
        JxlResizableParallelRunnerDestroy(runner);

    JxlDecoderDestroy(dec);

    if (!ok) {
        std::free(pixels);
        return false;
    }

    track_allocation(pixels);

    *out_pixels = pixels;
    return true;
}

} // extern "C"
