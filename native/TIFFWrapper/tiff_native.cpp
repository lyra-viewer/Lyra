#include <tiffio.h>
#include <cstdarg>
#include <cstdint>
#include <cstdio>
#include <cstdlib>
#include <cstring>
#include <mutex>

#ifdef _WIN32
#define TIFF_API __declspec(dllexport)
#else
#define TIFF_API __attribute__((visibility("default")))
#endif

#ifdef __clang__
#define THREAD_LOCAL __thread
#else
#define THREAD_LOCAL thread_local
#endif

static THREAD_LOCAL char last_tiff_error[1024] = "";

static void set_error(const char *fmt, ...) {
    va_list args;
    va_start(args, fmt);
    vsnprintf(last_tiff_error, sizeof(last_tiff_error), fmt, args);
    va_end(args);
}

// libtiff routes decode failures through a global error handler; capture the message into the
// thread-local buffer so the managed side can surface a useful reason. Warnings are dropped:
// real-world TIFFs trip plenty of benign warnings that are not decode failures.
static void tiff_error_handler(const char *module, const char *fmt, va_list ap) {
    char msg[768];
    vsnprintf(msg, sizeof(msg), fmt, ap);
    snprintf(last_tiff_error, sizeof(last_tiff_error), "%s: %s", module ? module : "tiff", msg);
}

static void tiff_warning_handler(const char *, const char *, va_list) {}

extern "C" {

TIFF_API const char *get_last_tiff_error() { return last_tiff_error; }

// Decodes any libtiff-supported TIFF into a tightly packed 8-bit RGBA buffer with top-left
// origin. libtiff handles every compression / photometric / planar / tiled variant internally.
// On little-endian targets the uint32 ABGR raster lays out as R,G,B,A bytes. 
// Returns false and sets get_last_tiff_error() on failure.
TIFF_API bool load_tiff_rgba(const char *path, uint8_t **out_pixels, int *width, int *height) {
    *out_pixels = nullptr;
    *width = 0;
    *height = 0;
    last_tiff_error[0] = '\0';

    static std::once_flag handlers_flag;
    std::call_once(handlers_flag, []() {
        TIFFSetErrorHandler(tiff_error_handler);
        TIFFSetWarningHandler(tiff_warning_handler);
    });

    TIFF *tif = TIFFOpen(path, "r");
    if (!tif) {
        if (last_tiff_error[0] == '\0')
            set_error("Failed to open TIFF: %s", path);
        return false;
    }

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
    uint32_t *raster = (uint32_t *) malloc(byte_count);
    if (!raster) {
        set_error("Failed to allocate %zu bytes for TIFF raster", byte_count);
        TIFFClose(tif);
        return false;
    }

    // stopOnError = 0: decode as much as possible rather than bailing on the first bad strip.
    if (!TIFFReadRGBAImageOriented(tif, w, h, raster, ORIENTATION_TOPLEFT, 0)) {
        if (last_tiff_error[0] == '\0')
            set_error("TIFFReadRGBAImageOriented failed for: %s", path);
        free(raster);
        TIFFClose(tif);
        return false;
    }

    TIFFClose(tif);
    *out_pixels = (uint8_t *) raster;
    *width = (int) w;
    *height = (int) h;
    return true;
}

TIFF_API void free_tiff_pixels(uint8_t *ptr) {
    if (ptr)
        free(ptr);
}
}
