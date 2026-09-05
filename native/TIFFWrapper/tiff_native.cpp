#include "tiff_native.h"

#include <tiffio.h>

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

static THREAD_LOCAL char last_tiff_error[1024] = "";

static void set_error(const char *fmt, ...) {
    va_list args;
    va_start(args, fmt);
    std::vsnprintf(last_tiff_error, sizeof(last_tiff_error), fmt, args);
    va_end(args);
}

static void clear_error() { last_tiff_error[0] = '\0'; }

static void tiff_error_handler(const char *module, const char *fmt, va_list ap) {
    char msg[768];
    std::vsnprintf(msg, sizeof(msg), fmt, ap);
    std::snprintf(last_tiff_error, sizeof(last_tiff_error), "%s: %s", module ? module : "tiff", msg);
}

static void tiff_warning_handler(const char *, const char *, va_list) {}

namespace {

    struct memory_tiff {
        const uint8_t *data;
        uint64_t size;
        uint64_t pos;
    };

    tmsize_t mem_read(thandle_t handle, void *buffer, tmsize_t count) {
        auto *m = static_cast<memory_tiff *>(handle);
        if (count <= 0)
            return 0;

        const uint64_t available = m->pos < m->size ? m->size - m->pos : 0;
        uint64_t want = static_cast<uint64_t>(count);
        if (want > available)
            want = available;

        if (want > 0)
            std::memcpy(buffer, m->data + m->pos, static_cast<size_t>(want));

        m->pos += want;
        return static_cast<tmsize_t>(want);
    }

    tmsize_t mem_write(thandle_t, void *, tmsize_t) { return 0; }

    toff_t mem_seek(thandle_t handle, toff_t offset, int whence) {
        auto *m = static_cast<memory_tiff *>(handle);

        uint64_t base;
        switch (whence) {
            case SEEK_CUR:
                base = m->pos;
                break;
            case SEEK_END:
                base = m->size;
                break;
            default:
                base = 0;
                break;
        }

        m->pos = (offset > m->size - base) ? m->size : base + offset;
        return m->pos;
    }

    int mem_close(thandle_t) { return 0; }

    toff_t mem_size(thandle_t handle) { return static_cast<memory_tiff *>(handle)->size; }

    int mem_map(thandle_t handle, void **base, toff_t *size) {
        auto *m = static_cast<memory_tiff *>(handle);
        *base = const_cast<uint8_t *>(m->data);
        *size = m->size;
        return 1;
    }

    void mem_unmap(thandle_t, void *, toff_t) {}

} // namespace

static bool decode_open_tiff(TIFF *tif, const char *label, uint8_t **out_pixels, int *width, int *height,
                             uint8_t **out_icc, int *out_icc_size) {
    uint32_t w = 0, h = 0;
    TIFFGetField(tif, TIFFTAG_IMAGEWIDTH, &w);
    TIFFGetField(tif, TIFFTAG_IMAGELENGTH, &h);
    if (w == 0 || h == 0) {
        set_error("Invalid TIFF dimensions: %ux%u", w, h);
        TIFFClose(tif);
        return false;
    }

    uint64_t pixel_count = (uint64_t) w * (uint64_t) h;
    if (pixel_count > (uint64_t) (SIZE_MAX / 4)) {
        set_error("TIFF dimensions too large: %ux%u", w, h);
        TIFFClose(tif);
        return false;
    }

    size_t byte_count = (size_t) pixel_count * 4;
    uint32_t *raster = (uint32_t *) std::malloc(byte_count);
    if (!raster) {
        set_error("Failed to allocate %zu bytes for TIFF raster", byte_count);
        TIFFClose(tif);
        return false;
    }

    // stopOnError = 0: decode as much as possible rather than bailing on the first bad strip.
    if (!TIFFReadRGBAImageOriented(tif, w, h, raster, ORIENTATION_TOPLEFT, 0)) {
        if (last_tiff_error[0] == '\0')
            set_error("TIFFReadRGBAImageOriented failed for: %s", label);

        std::free(raster);
        TIFFClose(tif);
        return false;
    }

    // Best effort: a file with no profile, and a profile too big to copy, both leave the
    // caller with null - an un-tagged image, which is exactly how it is treated downstream.
    if (out_icc && out_icc_size) {
        uint32_t icc_count = 0;
        void *icc_data = nullptr;
        if (TIFFGetField(tif, TIFFTAG_ICCPROFILE, &icc_count, &icc_data) == 1 && icc_count > 0 && icc_data) {
            uint8_t *icc_copy = (uint8_t *) std::malloc(icc_count);
            if (icc_copy) {
                std::memcpy(icc_copy, icc_data, icc_count);
                *out_icc = icc_copy;
                *out_icc_size = (int) icc_count;
            }
        }
    }

    TIFFClose(tif);
    *out_pixels = (uint8_t *) raster;
    *width = (int) w;
    *height = (int) h;
    return true;
}

// Zeroes the outputs, installs the error handlers and clears the last error. Both entry
// points do this before opening anything.
static void begin_tiff_load(uint8_t **out_pixels, int *width, int *height, uint8_t **out_icc, int *out_icc_size) {
    *out_pixels = nullptr;
    *width = 0;
    *height = 0;

    if (out_icc)
        *out_icc = nullptr;

    if (out_icc_size)
        *out_icc_size = 0;

    clear_error();

    static std::once_flag handlers_flag;
    std::call_once(handlers_flag, []() {
        TIFFSetErrorHandler(tiff_error_handler);
        TIFFSetWarningHandler(tiff_warning_handler);
    });
}

extern "C" {

TIFF_API const char *get_last_tiff_error(void) { return last_tiff_error; }

TIFF_API bool load_tiff_rgba(const char *path, uint8_t **out_pixels, int *width, int *height, uint8_t **out_icc, int *out_icc_size) {
    begin_tiff_load(out_pixels, width, height, out_icc, out_icc_size);

    if (!path) {
        set_error("No TIFF path given.");
        return false;
    }

    TIFF *tif = TIFFOpen(path, "r");
    if (!tif) {
        if (last_tiff_error[0] == '\0')
            set_error("Failed to open TIFF: %s", path);
        return false;
    }

    return decode_open_tiff(tif, path, out_pixels, width, height, out_icc, out_icc_size);
}

TIFF_API bool load_tiff_rgba_mem(const uint8_t *data, uint64_t size, uint8_t **out_pixels, int *width, int *height, uint8_t **out_icc, int *out_icc_size) {
    begin_tiff_load(out_pixels, width, height, out_icc, out_icc_size);

    if (!data || size == 0) {
        set_error("Empty TIFF buffer.");
        return false;
    }

    memory_tiff client = {data, size, 0};

    TIFF *tif = TIFFClientOpen("<memory>", "r", (thandle_t) &client, mem_read, mem_write, mem_seek, mem_close, mem_size, mem_map, mem_unmap);
    if (!tif) {
        if (last_tiff_error[0] == '\0')
            set_error("Failed to open TIFF from memory.");
        return false;
    }

    return decode_open_tiff(tif, "<memory>", out_pixels, width, height, out_icc, out_icc_size);
}

TIFF_API void free_tiff_pixels(uint8_t *ptr) {
    if (ptr)
        std::free(ptr);
}

} // extern "C"
