#include "basis_native.h"

#include "basis_universal/transcoder/basisu_transcoder.h"

#include <cstdarg>
#include <cstdint>
#include <cstdio>
#include <cstdlib>
#include <cstring>
#include <mutex>

#ifdef __clang__
#define THREAD_LOCAL __thread
#else
#define THREAD_LOCAL thread_local
#endif

static THREAD_LOCAL char last_basis_error[1024] = "";

static void set_error(const char *fmt, ...) {
    va_list args;
    va_start(args, fmt);
    std::vsnprintf(last_basis_error, sizeof(last_basis_error), fmt, args);
    va_end(args);
}

static void clear_error() { last_basis_error[0] = '\0'; }

// The transcoder's lookup tables are global and must be built exactly once before any transcode.
static void ensure_init() {
    static std::once_flag init_flag;
    std::call_once(init_flag, []() { basist::basisu_transcoder_init(); });
}

extern "C" {

BASIS_API const char *get_last_basis_error(void) { return last_basis_error; }

BASIS_API bool basis_decode_ktx2_rgba(const uint8_t *data, int size, int level, int layer, int face,
                                      uint8_t **out_pixels, int *out_width, int *out_height) {
    *out_pixels = nullptr;
    *out_width = 0;
    *out_height = 0;
    clear_error();

    if (!data || size <= 0) {
        set_error("Invalid KTX2 buffer (size %d)", size);
        return false;
    }

    if (level < 0 || layer < 0 || face < 0) {
        set_error("Invalid image selector: level %d, layer %d, face %d", level, layer, face);
        return false;
    }

    ensure_init();

    basist::ktx2_transcoder transcoder;
    if (!transcoder.init(data, (uint32_t) size)) {
        set_error("ktx2_transcoder::init failed (not a valid KTX2 Basis file)");
        return false;
    }

    if (!transcoder.start_transcoding()) {
        set_error("ktx2_transcoder::start_transcoding failed");
        return false;
    }

    basist::ktx2_image_level_info info;
    if (!transcoder.get_image_level_info(info, (uint32_t) level, (uint32_t) layer, (uint32_t) face)) {
        set_error("No image at level %d, layer %d, face %d", level, layer, face);
        return false;
    }

    uint32_t w = info.m_orig_width;
    uint32_t h = info.m_orig_height;
    if (w == 0 || h == 0) {
        set_error("Invalid image dimensions %ux%u", w, h);
        return false;
    }

    uint64_t pixel_count = (uint64_t) w * (uint64_t) h;
    if (pixel_count > (uint64_t) (SIZE_MAX / 4)) {
        set_error("Image dimensions too large: %ux%u", w, h);
        return false;
    }

    if (pixel_count > (uint64_t) UINT32_MAX) {
        set_error("Image has too many pixels to transcode: %ux%u", w, h);
        return false;
    }

    uint8_t *pixels = (uint8_t *) std::malloc((size_t) pixel_count * 4);
    if (!pixels) {
        set_error("Failed to allocate %llu bytes", (unsigned long long) (pixel_count * 4));
        return false;
    }

    // cTFRGBA32: 32bpp RGBA in raster order, R first byte. Pixel-unit pitch/rows for an RGBA target.
    bool ok = transcoder.transcode_image_level((uint32_t) level, (uint32_t) layer, (uint32_t) face, pixels,
                                               (uint32_t) pixel_count, basist::transcoder_texture_format::cTFRGBA32, 0,
                                               w, h);

    if (!ok) {
        set_error("transcode_image_level failed for level %d, layer %d, face %d", level, layer, face);
        std::free(pixels);
        return false;
    }

    *out_pixels = pixels;
    *out_width = (int) w;
    *out_height = (int) h;
    return true;
}

BASIS_API void basis_free_pixels(uint8_t *ptr) {
    if (ptr)
        std::free(ptr);
}

} // extern "C"
