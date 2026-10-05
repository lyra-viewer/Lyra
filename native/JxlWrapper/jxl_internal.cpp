#include "jxl_internal.h"

#include <cstdarg>
#include <cstdio>
#include <mutex>
#include <unordered_set>

#include <jxl/cms.h>
#include <jxl/color_encoding.h>
#include <jxl/encode.h>
#include <jxl/resizable_parallel_runner.h>

#ifdef __clang__
#define THREAD_LOCAL __thread
#else
#define THREAD_LOCAL thread_local
#endif

namespace jxl_internal {

static THREAD_LOCAL char error_message[512] = "";

static std::unordered_set<void *> allocated;
static std::mutex allocated_mutex;

void set_error(const char *fmt, ...) {
    va_list args;
    va_start(args, fmt);
    std::vsnprintf(error_message, sizeof(error_message), fmt, args);
    va_end(args);
}

void clear_error() { error_message[0] = '\0'; }

const char *last_error() { return error_message; }

void track_allocation(void *ptr) {
    std::lock_guard<std::mutex> lock(allocated_mutex);
    allocated.insert(ptr);
}

bool release_allocation(void *ptr) {
    std::lock_guard<std::mutex> lock(allocated_mutex);
    return allocated.erase(ptr) > 0;
}

bool configure_decoder(JxlDecoder *dec, void *runner) {
    if (JxlDecoderSetCms(dec, *JxlGetDefaultCms()) != JXL_DEC_SUCCESS) {
        set_error("JxlDecoderSetCms failed.");
        return false;
    }

    if (runner && JxlDecoderSetParallelRunner(dec, JxlResizableParallelRunner, runner) != JXL_DEC_SUCCESS) {
        set_error("JxlDecoderSetParallelRunner failed.");
        return false;
    }

    if (JxlDecoderSetUnpremultiplyAlpha(dec, JXL_TRUE) != JXL_DEC_SUCCESS) {
        set_error("JxlDecoderSetUnpremultiplyAlpha failed.");
        return false;
    }

    return true;
}

bool set_output_profile(JxlDecoder *dec, bool is_hdr) {
    JxlColorEncoding target{};
    if (is_hdr) {
        JxlColorEncodingSetToLinearSRGB(&target, JXL_FALSE);
    } else {
        target.color_space = JXL_COLOR_SPACE_RGB;
        target.white_point = JXL_WHITE_POINT_D65;
        target.primaries = JXL_PRIMARIES_P3;
        target.transfer_function = JXL_TRANSFER_FUNCTION_SRGB;
        target.rendering_intent = JXL_RENDERING_INTENT_RELATIVE;
    }

    if (JxlDecoderSetOutputColorProfile(dec, &target, nullptr, 0) != JXL_DEC_SUCCESS) {
        set_error("JxlDecoderSetOutputColorProfile failed.");
        return false;
    }

    return true;
}

bool is_hdr_image(const JxlBasicInfo &info) { return info.exponent_bits_per_sample > 0; }

JxlPixelFormat pixel_format(bool is_hdr) {
    JxlPixelFormat format{};
    format.num_channels = 4;
    format.data_type = is_hdr ? JXL_TYPE_FLOAT : JXL_TYPE_UINT8;
    format.endianness = JXL_NATIVE_ENDIAN;
    format.align = 0;
    return format;
}

} // namespace jxl_internal
