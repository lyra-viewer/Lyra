#include "jxl_native.h"
#include "jxl_internal.h"

#include <climits>
#include <cmath>
#include <cstdlib>
#include <new>
#include <vector>

#include <jxl/cms.h>
#include <jxl/decode.h>
#include <jxl/resizable_parallel_runner.h>

using namespace jxl_internal;

struct jxl_animation {
    std::vector<uint8_t> data;
    JxlBasicInfo info{};
    bool is_hdr = false;
    std::vector<int32_t> durations_ms;
    bool file_color_space = false;
    std::vector<uint8_t> icc;

    // The decoder stays positioned after the last frame it produced, so the next
    // one costs a single frame's decode. Null until first needed, and after a failure.
    JxlDecoder *dec = nullptr;
    void *runner = nullptr;
    int32_t next_frame = 0;
};

static void drop_decoder(jxl_animation *a) {
    if (a->dec)
        JxlDecoderDestroy(a->dec);

    a->dec = nullptr;
    a->next_frame = 0;
}

static bool start_decoder(jxl_animation *a) {
    drop_decoder(a);

    a->dec = JxlDecoderCreate(nullptr);
    if (!a->dec) {
        set_error("JxlDecoderCreate failed.");
        return false;
    }

    if (!configure_decoder(a->dec, a->runner)) {
        drop_decoder(a);
        return false;
    }

    if (JxlDecoderSubscribeEvents(a->dec, JXL_DEC_COLOR_ENCODING | JXL_DEC_FULL_IMAGE) != JXL_DEC_SUCCESS ||
        JxlDecoderSetInput(a->dec, a->data.data(), a->data.size()) != JXL_DEC_SUCCESS) {
        set_error("Could not set up the decoder.");
        drop_decoder(a);
        return false;
    }

    return true;
}

static int32_t ticks_to_ms(uint32_t ticks, const JxlAnimationHeader &animation) {
    if (animation.tps_numerator == 0)
        return 0;

    double ms = std::round((double) ticks * 1000.0 * animation.tps_denominator / animation.tps_numerator);
    return ms > INT32_MAX ? INT32_MAX : (int32_t) ms;
}

extern "C" {

JXL_NATIVE_API jxl_animation *jxl_animation_open(const uint8_t *data, size_t size, jxl_animation_info *out_info) {
    clear_error();

    if (out_info)
        *out_info = jxl_animation_info{};

    if (!data || size == 0 || !out_info) {
        set_error("Invalid arguments.");
        return nullptr;
    }

    JxlSignature sig = JxlSignatureCheck(data, size);
    if (sig != JXL_SIG_CODESTREAM && sig != JXL_SIG_CONTAINER) {
        set_error("Not a JPEG XL file.");
        return nullptr;
    }

    auto *a = new (std::nothrow) jxl_animation();
    if (!a) {
        set_error("Out of memory.");
        return nullptr;
    }

    // Headers only: without a JXL_DEC_FULL_IMAGE subscription libjxl skips each frame's pixels.
    JxlDecoder *scan = JxlDecoderCreate(nullptr);
    bool ok = scan && JxlDecoderSetCms(scan, *JxlGetDefaultCms()) == JXL_DEC_SUCCESS &&
              JxlDecoderSubscribeEvents(scan, JXL_DEC_BASIC_INFO | JXL_DEC_COLOR_ENCODING | JXL_DEC_FRAME) == JXL_DEC_SUCCESS &&
              JxlDecoderSetInput(scan, data, size) == JXL_DEC_SUCCESS;

    bool have_info = false;
    int32_t incomplete = JXL_ANIMATION_COMPLETE;

    if (!ok) {
        set_error("Could not set up the decoder.");
    } else {
        // Input left open, as in start_decoder, so a short file and a corrupt one differ.
        for (;;) {
            JxlDecoderStatus status = JxlDecoderProcessInput(scan);

            if (status == JXL_DEC_BASIC_INFO) {
                if (JxlDecoderGetBasicInfo(scan, &a->info) != JXL_DEC_SUCCESS) {
                    set_error("JxlDecoderGetBasicInfo failed.");
                    ok = false;
                    break;
                }

                if ((int) a->info.xsize <= 0 || (int) a->info.ysize <= 0) {
                    set_error("Invalid dimensions: %ux%u.", a->info.xsize, a->info.ysize);
                    ok = false;
                    break;
                }

                have_info = true;

                // A still image needs no frame walk: it has the one.
                if (!a->info.have_animation) {
                    a->durations_ms.push_back(0);
                    break;
                }

                a->file_color_space = !a->info.uses_original_profile;
                continue;
            }

            // With no output color space asked for, the data profile is the file's own.
            if (status == JXL_DEC_COLOR_ENCODING) {
                size_t icc_size = 0;
                if (a->file_color_space &&
                    JxlDecoderGetICCProfileSize(scan, JXL_COLOR_PROFILE_TARGET_DATA, &icc_size) == JXL_DEC_SUCCESS && icc_size > 0) {
                    try {
                        a->icc.resize(icc_size);
                    } catch (...) {
                        a->icc.clear();
                    }

                    if (!a->icc.empty() &&
                        JxlDecoderGetColorAsICCProfile(scan, JXL_COLOR_PROFILE_TARGET_DATA, a->icc.data(), icc_size) != JXL_DEC_SUCCESS)
                        a->icc.clear();
                }

                continue;
            }

            if (status == JXL_DEC_FRAME) {
                JxlFrameHeader header{};
                if (JxlDecoderGetFrameHeader(scan, &header) != JXL_DEC_SUCCESS) {
                    incomplete = JXL_ANIMATION_BROKEN;
                    break;
                }

                a->durations_ms.push_back(ticks_to_ms(header.duration, a->info.animation));

                if (header.is_last)
                    break;

                continue;
            }

            if (status == JXL_DEC_SUCCESS)
                break;

            // Short, or broken part-way: keep the frames whose headers were read.
            if (status == JXL_DEC_NEED_MORE_INPUT || status == JXL_DEC_ERROR) {
                incomplete = status == JXL_DEC_NEED_MORE_INPUT ? JXL_ANIMATION_TRUNCATED : JXL_ANIMATION_BROKEN;
                break;
            }

            set_error("Unexpected decoder status: %d.", (int) status);
            ok = false;
            break;
        }
    }

    if (scan)
        JxlDecoderDestroy(scan);

    if (ok && (!have_info || a->durations_ms.empty())) {
        set_error(have_info ? "The stream ends before its first frame." : "The stream ends before its header.");
        ok = false;
    }

    if (ok && a->info.have_animation) {
        try {
            a->data.assign(data, data + size);
        } catch (...) {
            set_error("Out of memory: %zu bytes.", size);
            ok = false;
        }
    }

    if (!ok) {
        delete a;
        return nullptr;
    }

    a->is_hdr = is_hdr_image(a->info);
    a->runner = JxlResizableParallelRunnerCreate(nullptr);
    if (a->runner)
        JxlResizableParallelRunnerSetThreads(a->runner, JxlResizableParallelRunnerSuggestThreads(a->info.xsize, a->info.ysize));

    // Upright already, as in decode_jxl_from_memory.
    out_info->width = (int32_t) a->info.xsize;
    out_info->height = (int32_t) a->info.ysize;
    out_info->is_hdr = a->is_hdr ? 1 : 0;
    out_info->bits_per_sample = (int32_t) a->info.bits_per_sample;
    out_info->has_alpha = a->info.alpha_bits > 0 ? 1 : 0;
    out_info->frame_count = (int32_t) a->durations_ms.size();
    out_info->loop_count = a->info.animation.num_loops > INT32_MAX ? INT32_MAX : (int32_t) a->info.animation.num_loops;
    out_info->incomplete = incomplete;
    out_info->file_color_space = a->file_color_space ? 1 : 0;

    return a;
}

JXL_NATIVE_API bool jxl_animation_frame_durations(const jxl_animation *animation, int32_t *out_ms, int32_t count) {
    clear_error();

    if (!animation || !out_ms || count < 0 || (size_t) count > animation->durations_ms.size()) {
        set_error("Invalid arguments.");
        return false;
    }

    for (int32_t i = 0; i < count; i++)
        out_ms[i] = animation->durations_ms[i];

    return true;
}

JXL_NATIVE_API bool jxl_animation_decode_frame(jxl_animation *animation, int32_t index, uint8_t **out_pixels,
                                               int32_t *out_partial) {
    clear_error();

    if (out_pixels)
        *out_pixels = nullptr;

    if (out_partial)
        *out_partial = 0;

    if (!animation || !out_pixels || !out_partial || index < 0 || (size_t) index >= animation->durations_ms.size()) {
        set_error("Invalid arguments.");
        return false;
    }

    jxl_animation *a = animation;

    if (a->data.empty()) {
        set_error("A still image has no frames to decode here.");
        return false;
    }

    // Behind the decoder's position: start again from the top, skipping forward.
    if (!a->dec || index < a->next_frame) {
        if (!start_decoder(a))
            return false;
    }

    if (index > a->next_frame) {
        JxlDecoderSkipFrames(a->dec, (size_t) (index - a->next_frame));
        a->next_frame = index;
    }

    const JxlPixelFormat format = pixel_format(a->is_hdr);
    const size_t expected = (size_t) a->info.xsize * a->info.ysize * 4 * (a->is_hdr ? sizeof(float) : sizeof(uint8_t));
    uint8_t *pixels = nullptr;
    bool ok = false;

    for (;;) {
        JxlDecoderStatus status = JxlDecoderProcessInput(a->dec);

        if (status == JXL_DEC_COLOR_ENCODING) {
            if (!a->file_color_space && !set_output_profile(a->dec, a->is_hdr))
                break;

            continue;
        }

        if (status == JXL_DEC_NEED_IMAGE_OUT_BUFFER) {
            size_t buffer_size = 0;
            if (JxlDecoderImageOutBufferSize(a->dec, &format, &buffer_size) != JXL_DEC_SUCCESS || buffer_size != expected) {
                set_error("Unexpected output buffer size: %zu, expected %zu.", buffer_size, expected);
                break;
            }

            // calloc: what a cut-short frame never reaches stays transparent.
            pixels = (uint8_t *) std::calloc(1, buffer_size);
            if (!pixels) {
                set_error("Out of memory: %zu bytes for a %ux%u frame.", buffer_size, a->info.xsize, a->info.ysize);
                break;
            }

            if (JxlDecoderSetImageOutBuffer(a->dec, &format, pixels, buffer_size) != JXL_DEC_SUCCESS) {
                set_error("JxlDecoderSetImageOutBuffer failed.");
                break;
            }

            continue;
        }

        if (status == JXL_DEC_FULL_IMAGE) {
            ok = pixels != nullptr;
            if (!ok)
                set_error("No pixel buffer produced.");
            else
                a->next_frame = index + 1;

            break;
        }

        // All the data was given, so the stream is short: keep what the frame has, if libjxl can give it.
        if (status == JXL_DEC_NEED_MORE_INPUT) {
            if (pixels && JxlDecoderFlushImage(a->dec) == JXL_DEC_SUCCESS) {
                *out_partial = 1;
                ok = true;
            } else {
                set_error("Frame %d is cut short: the file ends inside it", index + 1);
            }

            break;
        }

        if (status == JXL_DEC_SUCCESS) {
            set_error("The stream ends before frame %d", index + 1);
            break;
        }

        if (status == JXL_DEC_ERROR) {
            set_error("Frame %d is corrupt", index + 1);
            break;
        }

        set_error("Unexpected decoder status: %d.", (int) status);
        break;
    }

    // Anything but a whole frame leaves the decoder somewhere it cannot continue from.
    if (!ok || *out_partial)
        drop_decoder(a);

    if (!ok) {
        std::free(pixels);
        return false;
    }

    track_allocation(pixels);
    *out_pixels = pixels;
    return true;
}

JXL_NATIVE_API bool jxl_animation_icc(const jxl_animation *animation, const uint8_t **out_icc, size_t *out_size) {
    clear_error();

    if (out_icc)
        *out_icc = nullptr;

    if (out_size)
        *out_size = 0;

    if (!animation || !out_icc || !out_size) {
        set_error("Invalid arguments.");
        return false;
    }

    if (animation->icc.empty()) {
        set_error("The frames carry no ICC profile.");
        return false;
    }

    *out_icc = animation->icc.data();
    *out_size = animation->icc.size();
    return true;
}

JXL_NATIVE_API void jxl_animation_close(jxl_animation *animation) {
    if (!animation)
        return;

    drop_decoder(animation);

    if (animation->runner)
        JxlResizableParallelRunnerDestroy(animation->runner);

    delete animation;
}

} // extern "C"
