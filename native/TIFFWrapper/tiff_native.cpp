#include "tiff_native.h"

#include <tiffio.h>

#include <cstdarg>
#include <cstdint>
#include <cstdio>
#include <cstdlib>
#include <cstring>
#include <mutex>
#include <vector>
#include <algorithm>

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

static bool fits_allocation(uint64_t bytes) {
    return bytes <= static_cast<uint64_t>(SIZE_MAX);
}

static bool mul_overflows(uint64_t a, uint64_t b, uint64_t *out) {
    if (a != 0 && b > UINT64_MAX / a)
        return true;

    *out = a * b;
    return false;
}

static void tiff_error_handler(const char *module, const char *fmt, va_list ap) {
    char msg[768];
    std::vsnprintf(msg, sizeof(msg), fmt, ap);
    std::snprintf(last_tiff_error, sizeof(last_tiff_error), "%s: %s", module ? module : "tiff", msg);
}

static void tiff_warning_handler(const char *, const char *, va_list) {}

static void install_handlers() {
    static std::once_flag handlers_flag;
    std::call_once(handlers_flag, []() {
        TIFFSetErrorHandler(tiff_error_handler);
        TIFFSetWarningHandler(tiff_warning_handler);
    });
}

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

namespace {

    bool directory_is_gray(TIFF *tif) {
        uint16_t samples = 1, bits = 1, format = SAMPLEFORMAT_UINT, photometric = 0;

        TIFFGetFieldDefaulted(tif, TIFFTAG_SAMPLESPERPIXEL, &samples);
        TIFFGetFieldDefaulted(tif, TIFFTAG_BITSPERSAMPLE, &bits);
        TIFFGetFieldDefaulted(tif, TIFFTAG_SAMPLEFORMAT, &format);

        if (!TIFFGetField(tif, TIFFTAG_PHOTOMETRIC, &photometric))
            return false;

        return samples == 1
               && (bits == 1 || bits == 8 || bits == 16)
               && format == SAMPLEFORMAT_UINT
               && (photometric == PHOTOMETRIC_MINISWHITE || photometric == PHOTOMETRIC_MINISBLACK);
    }

    void expand_row(const uint8_t *src, uint16_t bits, bool invert, uint32_t first, uint32_t count, uint8_t *dst) {
        switch (bits) {
            case 1:
                for (uint32_t i = 0; i < count; i++) {
                    const uint32_t bit = first + i;
                    const uint8_t set = (src[bit >> 3] >> (7 - (bit & 7))) & 1;
                    dst[i] = static_cast<uint8_t>((set != 0) != invert ? 255 : 0);
                }
                break;

            case 8:
                for (uint32_t i = 0; i < count; i++) {
                    const uint8_t v = src[first + i];
                    dst[i] = invert ? static_cast<uint8_t>(255 - v) : v;
                }
                break;

            case 16:
                // libtiff has already put these in host order; the high byte is the grey level.
                for (uint32_t i = 0; i < count; i++) {
                    const uint16_t v = reinterpret_cast<const uint16_t *>(src)[first + i];
                    const uint8_t g = static_cast<uint8_t>(v >> 8);
                    dst[i] = invert ? static_cast<uint8_t>(255 - g) : g;
                }
                break;

            default:
                std::memset(dst, 0, count);
                break;
        }
    }

    // Whether the general path can read this directory. Photometric is the limit: grey and RGB are
    // arithmetic, while palette, YCbCr, CMYK and L*a*b* are conversions the RGBA interface owns.
    bool directory_is_native(TIFF *tif) {
        uint16_t samples = 1, bits = 1, format = SAMPLEFORMAT_UINT, photometric = 0;

        TIFFGetFieldDefaulted(tif, TIFFTAG_SAMPLESPERPIXEL, &samples);
        TIFFGetFieldDefaulted(tif, TIFFTAG_BITSPERSAMPLE, &bits);
        TIFFGetFieldDefaulted(tif, TIFFTAG_SAMPLEFORMAT, &format);

        if (!TIFFGetField(tif, TIFFTAG_PHOTOMETRIC, &photometric))
            return false;

        if (photometric != PHOTOMETRIC_MINISWHITE && photometric != PHOTOMETRIC_MINISBLACK &&
            photometric != PHOTOMETRIC_RGB)
            return false;

        if (samples < 1 || samples > 64 || bits < 1 || bits > 64)
            return false;

        if (format == SAMPLEFORMAT_IEEEFP)
            return bits == 16 || bits == 32 || bits == 64;

        return format == SAMPLEFORMAT_UINT || format == SAMPLEFORMAT_INT;
    }

    // Whether a rectangle can be read as RGBA.
    bool directory_is_rgb_region(TIFF *tif) {
        uint16_t samples = 1, bits = 1, planar = PLANARCONFIG_CONTIG, format = SAMPLEFORMAT_UINT, photometric = 0;

        TIFFGetFieldDefaulted(tif, TIFFTAG_SAMPLESPERPIXEL, &samples);
        TIFFGetFieldDefaulted(tif, TIFFTAG_BITSPERSAMPLE, &bits);
        TIFFGetFieldDefaulted(tif, TIFFTAG_PLANARCONFIG, &planar);
        TIFFGetFieldDefaulted(tif, TIFFTAG_SAMPLEFORMAT, &format);

        if (!TIFFGetField(tif, TIFFTAG_PHOTOMETRIC, &photometric))
            return false;

        return photometric == PHOTOMETRIC_RGB && bits == 8 && (samples == 3 || samples == 4)
               && planar == PLANARCONFIG_CONTIG && format == SAMPLEFORMAT_UINT;
    }

    enum class rgb_alpha { none, unassociated, associated };

    rgb_alpha rgb_region_alpha(TIFF *tif, uint16_t samples) {
        if (samples < 4)
            return rgb_alpha::none;

        uint16_t count = 0;
        uint16_t *kinds = nullptr;

        if (!TIFFGetField(tif, TIFFTAG_EXTRASAMPLES, &count, &kinds) || count == 0 || kinds == nullptr)
            return rgb_alpha::none;

        switch (kinds[0]) {
            case EXTRASAMPLE_ASSOCALPHA: return rgb_alpha::associated;
            case EXTRASAMPLE_UNASSALPHA: return rgb_alpha::unassociated;
            default: return rgb_alpha::none;
        }
    }

    void expand_row_rgba(const uint8_t *src, uint16_t samples, bool opaque, uint32_t first, uint32_t count, uint8_t *dst) {
        if (samples == 4 && !opaque) {
            std::memcpy(dst, src + static_cast<size_t>(first) * 4, static_cast<size_t>(count) * 4);
            return;
        }

        const uint8_t *p = src + static_cast<size_t>(first) * samples;

        for (uint32_t i = 0; i < count; i++, p += samples) {
            dst[i * 4 + 0] = p[0];
            dst[i * 4 + 1] = p[1];
            dst[i * 4 + 2] = p[2];
            dst[i * 4 + 3] = 255;
        }
    }

    float half_to_float(uint16_t h) {
        const uint32_t sign = static_cast<uint32_t>(h & 0x8000u) << 16;
        uint32_t exponent = (h >> 10) & 0x1F;
        uint32_t mantissa = h & 0x3FF;

        if (exponent == 0) {
            if (mantissa == 0) {
                const uint32_t bits = sign;
                float out;
                std::memcpy(&out, &bits, sizeof(out));
                return out;
            }

            while ((mantissa & 0x400) == 0) {
                mantissa <<= 1;
                exponent--;
            }

            exponent++;
            mantissa &= 0x3FF;
        } else if (exponent == 31) {
            const uint32_t bits = sign | 0x7F800000u | (mantissa << 13);
            float out;
            std::memcpy(&out, &bits, sizeof(out));
            return out;
        }

        const uint32_t bits = sign | ((exponent + 112) << 23) | (mantissa << 13);
        float out;
        std::memcpy(&out, &bits, sizeof(out));
        return out;
    }

    float read_sample(const uint8_t *row, uint64_t index, uint16_t bits, uint16_t format) {
        if (format == SAMPLEFORMAT_IEEEFP) {
            switch (bits) {
                case 16: return half_to_float(reinterpret_cast<const uint16_t *>(row)[index]);
                case 32: return reinterpret_cast<const float *>(row)[index];
                case 64: return static_cast<float>(reinterpret_cast<const double *>(row)[index]);
                default: return 0.0f;
            }
        }

        uint64_t raw = 0;

        if (bits == 8) {
            raw = row[index];
        } else if (bits == 16) {
            raw = reinterpret_cast<const uint16_t *>(row)[index];
        } else if (bits == 32) {
            raw = reinterpret_cast<const uint32_t *>(row)[index];
        } else {
            // Generic bit extraction, MSB first.
            uint64_t bit = index * bits;

            for (uint16_t i = 0; i < bits; i++, bit++)
                raw = (raw << 1) | ((row[bit >> 3] >> (7 - (bit & 7))) & 1);
        }

        const double span = bits >= 64 ? 1.8446744073709552e19 : static_cast<double>((1ull << bits) - 1);

        if (format == SAMPLEFORMAT_INT) {
            // Sign-extend, then place the signed range across 0..1 so the midpoint is mid-grey.
            const uint64_t signBit = 1ull << (bits - 1);
            const int64_t value = (raw & signBit) ? static_cast<int64_t>(raw) - static_cast<int64_t>(signBit << 1)
                                                  : static_cast<int64_t>(raw);

            return static_cast<float>((static_cast<double>(value) + (span + 1) / 2.0) / span);
        }

        return static_cast<float>(static_cast<double>(raw) / span);
    }

} // namespace

static bool seek_directory(TIFF *tif, const char *label, int directory) {
    if (directory < 0 || directory > 0xFFFF) {
        set_error("No directory %d in: %s", directory, label);
        return false;
    }

    // Directory 0 is already current after opening, so only a real page turn costs anything.
    if (directory > 0 && !TIFFSetDirectory(tif, static_cast<uint16_t>(directory))) {
        if (last_tiff_error[0] == '\0')
            set_error("No directory %d in: %s", directory, label);

        return false;
    }

    return true;
}

static bool decode_open_tiff(TIFF *tif, const char *label, int directory, uint8_t **out_pixels, int *width, int *height, uint8_t **out_icc, int *out_icc_size) {
    if (!seek_directory(tif, label, directory)) {
        TIFFClose(tif);
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

// Zeroes the outputs, installs the error handlers and clears the last error.
static void begin_tiff_load(uint8_t **out_pixels, int *width, int *height, uint8_t **out_icc, int *out_icc_size) {
    *out_pixels = nullptr;
    *width = 0;
    *height = 0;

    if (out_icc)
        *out_icc = nullptr;

    if (out_icc_size)
        *out_icc_size = 0;

    clear_error();
    install_handlers();
}

extern "C" {

TIFF_API const char *get_last_tiff_error(void) { return last_tiff_error; }

TIFF_API bool load_tiff_rgba_at(const char *path, int directory, uint8_t **out_pixels, int *width, int *height, uint8_t **out_icc, int *out_icc_size) {
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

    return decode_open_tiff(tif, path, directory, out_pixels, width, height, out_icc, out_icc_size);
}

TIFF_API bool load_tiff_rgba_mem_at(const uint8_t *data, uint64_t size, int directory, uint8_t **out_pixels, int *width, int *height, uint8_t **out_icc, int *out_icc_size) {
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

    return decode_open_tiff(tif, "<memory>", directory, out_pixels, width, height, out_icc, out_icc_size);
}

TIFF_API bool load_tiff_rgba(const char *path, uint8_t **out_pixels, int *width, int *height, uint8_t **out_icc, int *out_icc_size) {
    return load_tiff_rgba_at(path, 0, out_pixels, width, height, out_icc, out_icc_size);
}

TIFF_API bool load_tiff_rgba_mem(const uint8_t *data, uint64_t size, uint8_t **out_pixels, int *width, int *height, uint8_t **out_icc, int *out_icc_size) {
    return load_tiff_rgba_mem_at(data, size, 0, out_pixels, width, height, out_icc, out_icc_size);
}

static bool describe_open_tiff(TIFF *tif, TiffDirectoryInfo **out_dirs, int *out_count) {
    std::vector<TiffDirectoryInfo> found;

    do {
        TiffDirectoryInfo info;
        std::memset(&info, 0, sizeof(info));

        TIFFGetField(tif, TIFFTAG_IMAGEWIDTH, &info.width);
        TIFFGetField(tif, TIFFTAG_IMAGELENGTH, &info.height);

        TIFFGetField(tif, TIFFTAG_SUBFILETYPE, &info.subfile_type);
        TIFFGetField(tif, TIFFTAG_PAGENUMBER, &info.page_number, &info.page_total);

        TIFFGetFieldDefaulted(tif, TIFFTAG_BITSPERSAMPLE, &info.bits_per_sample);
        TIFFGetFieldDefaulted(tif, TIFFTAG_SAMPLESPERPIXEL, &info.samples_per_pixel);
        TIFFGetFieldDefaulted(tif, TIFFTAG_SAMPLEFORMAT, &info.sample_format);
        TIFFGetFieldDefaulted(tif, TIFFTAG_PLANARCONFIG, &info.planar_config);
        TIFFGetFieldDefaulted(tif, TIFFTAG_COMPRESSION, &info.compression);
        TIFFGetField(tif, TIFFTAG_PHOTOMETRIC, &info.photometric);

        info.is_tiled = TIFFIsTiled(tif) ? 1 : 0;
        info.gray_capable = directory_is_gray(tif) ? 1 : 0;
        info.native_capable = directory_is_native(tif) ? 1 : 0;

        const bool grayRegion = info.gray_capable != 0;
        const bool rgbRegion = directory_is_rgb_region(tif);

        info.region_capable = (grayRegion || rgbRegion) ? 1 : 0;
        info.region_samples = grayRegion ? 1 : (rgbRegion ? 4 : 0);
        info.region_premul = (rgbRegion && rgb_region_alpha(tif, info.samples_per_pixel) == rgb_alpha::associated) ? 1 : 0;

        char why[1024] = "";
        info.rgba_capable = TIFFRGBAImageOK(tif, why) ? 1 : 0;

        if (info.is_tiled) {
            TIFFGetField(tif, TIFFTAG_TILEWIDTH, &info.tile_width);
            TIFFGetField(tif, TIFFTAG_TILELENGTH, &info.tile_height);
        } else {
            TIFFGetFieldDefaulted(tif, TIFFTAG_ROWSPERSTRIP, &info.rows_per_strip);
        }

        found.push_back(info);

        // Second line of defence against a malformed chain that loops. The cap is the 16-bit
        // directory index, past which no page reported here could be loaded anyway.
        if (found.size() > 0xFFFF)
            break;
    } while (TIFFReadDirectory(tif));

    TIFFClose(tif);

    if (found.empty()) {
        set_error("TIFF has no directories.");
        return false;
    }

    const size_t bytes = found.size() * sizeof(TiffDirectoryInfo);
    auto *copy = static_cast<TiffDirectoryInfo *>(std::malloc(bytes));
    if (!copy) {
        set_error("Failed to allocate %zu bytes for %zu directories.", bytes, found.size());
        return false;
    }

    std::memcpy(copy, found.data(), bytes);
    *out_dirs = copy;
    *out_count = static_cast<int>(found.size());
    return true;
}

// Zeroes the outputs and installs the handlers, as begin_tiff_load does for the decode path.
static void begin_describe(TiffDirectoryInfo **out_dirs, int *out_count) {
    if (out_dirs)
        *out_dirs = nullptr;

    if (out_count)
        *out_count = 0;

    clear_error();
    install_handlers();
}

TIFF_API bool describe_tiff_directories(const char *path, TiffDirectoryInfo **out_dirs, int *out_count) {
    begin_describe(out_dirs, out_count);

    if (!path || !out_dirs || !out_count) {
        set_error("No TIFF path given.");
        return false;
    }

    TIFF *tif = TIFFOpen(path, "r");
    if (!tif) {
        if (last_tiff_error[0] == '\0')
            set_error("Failed to open TIFF: %s", path);
        return false;
    }

    return describe_open_tiff(tif, out_dirs, out_count);
}

TIFF_API bool describe_tiff_directories_mem(const uint8_t *data, uint64_t size, TiffDirectoryInfo **out_dirs, int *out_count) {
    begin_describe(out_dirs, out_count);

    if (!data || size == 0 || !out_dirs || !out_count) {
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

    return describe_open_tiff(tif, out_dirs, out_count);
}

TIFF_API void free_tiff_directories(TiffDirectoryInfo *ptr) {
    if (ptr)
        std::free(ptr);
}

static tmsize_t read_strip(TIFF *tif, uint32_t strip, uint8_t *buf, tmsize_t capacity, uint16_t bits) {
    const tmsize_t decoded = TIFFReadEncodedStrip(tif, strip, buf, capacity);
    if (decoded >= 0)
        return decoded;

    uint16_t compression = COMPRESSION_NONE;
    TIFFGetFieldDefaulted(tif, TIFFTAG_COMPRESSION, &compression);

    if (compression != COMPRESSION_NONE)
        return -1;

    clear_error();

    const tmsize_t raw = TIFFReadRawStrip(tif, strip, buf, capacity);
    if (raw < 0)
        return -1;

    if (TIFFIsByteSwapped(tif)) {
        if (bits == 16)
            TIFFSwabArrayOfShort(reinterpret_cast<uint16_t *>(buf), static_cast<tmsize_t>(raw / 2));
        else if (bits == 32)
            TIFFSwabArrayOfLong(reinterpret_cast<uint32_t *>(buf), static_cast<tmsize_t>(raw / 4));
        else if (bits == 64)
            TIFFSwabArrayOfDouble(reinterpret_cast<double *>(buf), static_cast<tmsize_t>(raw / 8));
    }

    return raw;
}

static bool read_region(TIFF *tif, uint32_t x, uint32_t y, uint32_t width, uint32_t height, bool rgba, uint8_t **out_pixels, uint32_t *out_stride) {
    uint32_t image_width = 0, image_height = 0;
    TIFFGetField(tif, TIFFTAG_IMAGEWIDTH, &image_width);
    TIFFGetField(tif, TIFFTAG_IMAGELENGTH, &image_height);

    if (x >= image_width || y >= image_height) {
        set_error("Region origin %u,%u is outside %ux%u.", x, y, image_width, image_height);
        return false;
    }

    if (width > image_width - x)
        width = image_width - x;

    if (height > image_height - y)
        height = image_height - y;

    uint16_t bits = 1, samples = 1, photometric = PHOTOMETRIC_MINISBLACK;
    TIFFGetFieldDefaulted(tif, TIFFTAG_BITSPERSAMPLE, &bits);
    TIFFGetFieldDefaulted(tif, TIFFTAG_SAMPLESPERPIXEL, &samples);
    TIFFGetField(tif, TIFFTAG_PHOTOMETRIC, &photometric);

    const bool invert = !rgba && photometric == PHOTOMETRIC_MINISWHITE;
    const bool opaque = rgba && rgb_region_alpha(tif, samples) == rgb_alpha::none;
    const uint32_t outPixel = rgba ? 4 : 1;

    // 64-bit throughout: the whole point is images whose pixel count overflows 32 bits.
    uint64_t bytes = 0;
    if (mul_overflows(static_cast<uint64_t>(width) * height, outPixel, &bytes) || bytes == 0 || !fits_allocation(bytes)) {
        set_error("Region %ux%u does not fit in memory.", width, height);
        return false;
    }

    // The stride goes back as a 32-bit figure, so a wider row cannot be described even if held.
    const uint64_t outStride = static_cast<uint64_t>(width) * outPixel;
    if (outStride > UINT32_MAX) {
        set_error("Region row of %llu bytes does not fit a 32-bit stride.", (unsigned long long) outStride);
        return false;
    }

    auto *out = static_cast<uint8_t *>(std::malloc(static_cast<size_t>(bytes)));
    if (!out) {
        set_error("Failed to allocate %llu bytes for a %ux%u region.", (unsigned long long) bytes, width, height);
        return false;
    }

    bool ok = true;

    if (TIFFIsTiled(tif)) {
        uint32_t tile_width = 0, tile_height = 0;
        TIFFGetField(tif, TIFFTAG_TILEWIDTH, &tile_width);
        TIFFGetField(tif, TIFFTAG_TILELENGTH, &tile_height);

        // Zeroed, not just allocated: a truncated file reads a tile short and the rows past that
        // point are copied out anyway, so uninitialised would put stale heap on screen.
        const tmsize_t tile_bytes = TIFFTileSize(tif);
        auto *tile = static_cast<uint8_t *>(std::calloc(static_cast<size_t>(tile_bytes), 1));

        if (!tile || tile_width == 0 || tile_height == 0) {
            set_error("Could not set up a %lld byte tile buffer.", (long long) tile_bytes);
            std::free(tile);
            ok = false;
        } else {
            const uint64_t tile_row_bytes = (static_cast<uint64_t>(tile_width) * bits * samples + 7) / 8;

            for (uint32_t ty = y / tile_height * tile_height; ty < y + height && ok; ty += tile_height)
            for (uint32_t tx = x / tile_width * tile_width; tx < x + width && ok; tx += tile_width) {
                if (TIFFReadEncodedTile(tif, TIFFComputeTile(tif, tx, ty, 0, 0), tile, tile_bytes) < 0) {
                    if (last_tiff_error[0] == '\0')
                        set_error("Failed to read the tile at %u,%u.", tx, ty);
                    ok = false;
                    break;
                }

                // The overlap between this tile and the region asked for.
                const uint32_t left = tx > x ? tx : x;
                const uint32_t top = ty > y ? ty : y;
                const uint32_t right = (tx + tile_width < x + width) ? tx + tile_width : x + width;
                const uint32_t bottom = (ty + tile_height < y + height) ? ty + tile_height : y + height;

                for (uint32_t row = top; row < bottom; row++) {
                    const uint8_t *src = tile + (row - ty) * tile_row_bytes;
                    uint8_t *dst = out + static_cast<uint64_t>(row - y) * outStride + static_cast<uint64_t>(left - x) * outPixel;

                    if (rgba)
                        expand_row_rgba(src, samples, opaque, left - tx, right - left, dst);
                    else
                        expand_row(src, bits, invert, left - tx, right - left, dst);
                }
            }

            std::free(tile);
        }
    } else {
        uint32_t rows_per_strip = 1;
        TIFFGetFieldDefaulted(tif, TIFFTAG_ROWSPERSTRIP, &rows_per_strip);

        if (rows_per_strip == 0 || rows_per_strip > image_height)
            rows_per_strip = image_height;

        const tmsize_t strip_bytes = TIFFStripSize(tif);
        auto *strip = static_cast<uint8_t *>(std::calloc(static_cast<size_t>(strip_bytes), 1));

        if (!strip) {
            set_error("Failed to allocate a %lld byte strip buffer.", (long long) strip_bytes);
            ok = false;
        } else {
            const uint64_t strip_row_bytes = (static_cast<uint64_t>(image_width) * bits * samples + 7) / 8;

            for (uint32_t sy = y / rows_per_strip * rows_per_strip; sy < y + height && ok; sy += rows_per_strip) {
                if (read_strip(tif, TIFFComputeStrip(tif, sy, 0), strip, strip_bytes, bits) < 0) {
                    if (last_tiff_error[0] == '\0')
                        set_error("Failed to read the strip at row %u.", sy);
                    ok = false;
                    break;
                }

                const uint32_t top = sy > y ? sy : y;
                const uint32_t bottom = (sy + rows_per_strip < y + height) ? sy + rows_per_strip : y + height;

                for (uint32_t row = top; row < bottom; row++) {
                    const uint8_t *src = strip + (row - sy) * strip_row_bytes;
                    uint8_t *dst = out + static_cast<uint64_t>(row - y) * outStride;

                    if (rgba)
                        expand_row_rgba(src, samples, opaque, x, width, dst);
                    else
                        expand_row(src, bits, invert, x, width, dst);
                }
            }

            std::free(strip);
        }
    }

    if (!ok) {
        std::free(out);
        return false;
    }

    *out_pixels = out;
    *out_stride = static_cast<uint32_t>(outStride);
    return true;
}

// Zeroes the outputs and refuses an empty rectangle, before anything is opened. Zeroed first
// because the header promises every out-parameter is zeroed on failure, whichever failure it is.
static bool begin_region(uint8_t **out_pixels, uint32_t *out_stride, uint32_t width, uint32_t height) {
    install_handlers();
    clear_error();

    if (out_pixels)
        *out_pixels = nullptr;

    if (out_stride)
        *out_stride = 0;

    if (!out_pixels || !out_stride) {
        set_error("No output given.");
        return false;
    }

    if (width == 0 || height == 0) {
        set_error("Empty region requested: %ux%u", width, height);
        return false;
    }

    return true;
}

static TIFF *open_for_region(const char *path, int directory, bool rgba) {
    if (!path) {
        set_error("No TIFF path given.");
        return nullptr;
    }

    TIFF *tif = TIFFOpen(path, "r");
    if (!tif) {
        if (last_tiff_error[0] == '\0')
            set_error("Failed to open TIFF: %s", path);
        return nullptr;
    }

    if (!seek_directory(tif, path, directory)) {
        TIFFClose(tif);
        return nullptr;
    }

    const bool suitable = rgba ? directory_is_rgb_region(tif) : directory_is_gray(tif);

    if (!suitable) {
        set_error("Directory %d is not %s.", directory, rgba ? "eight-bit contiguous colour" : "single-channel integer grey");
        TIFFClose(tif);
        return nullptr;
    }

    return tif;
}

TIFF_API bool load_tiff_gray_region(const char *path, int directory, uint32_t x, uint32_t y, uint32_t width, uint32_t height, uint8_t **out_pixels, uint32_t *out_stride) {
    if (!begin_region(out_pixels, out_stride, width, height))
        return false;

    TIFF *tif = open_for_region(path, directory, false);
    if (!tif)
        return false;

    const bool ok = read_region(tif, x, y, width, height, false, out_pixels, out_stride);
    TIFFClose(tif);
    return ok;
}

TIFF_API bool load_tiff_rgba_region(const char *path, int directory, uint32_t x, uint32_t y, uint32_t width, uint32_t height, uint8_t **out_pixels, uint32_t *out_stride) {
    if (!begin_region(out_pixels, out_stride, width, height))
        return false;

    TIFF *tif = open_for_region(path, directory, true);
    if (!tif)
        return false;

    const bool ok = read_region(tif, x, y, width, height, true, out_pixels, out_stride);
    TIFFClose(tif);
    return ok;
}

TIFF_API bool load_tiff_native(const char *path, int directory, int output_kind, uint8_t **out_pixels, int *out_width, int *out_height, uint32_t *out_stride) {
    install_handlers();
    clear_error();

    if (!out_pixels || !out_width || !out_height || !out_stride) {
        set_error("No output given.");
        return false;
    }

    *out_pixels = nullptr;
    *out_width = 0;
    *out_height = 0;
    *out_stride = 0;

    if (!path) {
        set_error("No TIFF path given.");
        return false;
    }

    if (output_kind != TIFF_OUT_GRAY8 && output_kind != TIFF_OUT_RGBA8 && output_kind != TIFF_OUT_RGBA_F32) {
        set_error("Unknown output kind: %d", output_kind);
        return false;
    }

    TIFF *tif = TIFFOpen(path, "r");
    if (!tif) {
        if (last_tiff_error[0] == '\0')
            set_error("Failed to open TIFF: %s", path);

        return false;
    }

    if (!seek_directory(tif, path, directory)) {
        TIFFClose(tif);
        return false;
    }

    if (!directory_is_native(tif)) {
        set_error("Directory %d is not a layout the native path reads.", directory);
        TIFFClose(tif);
        return false;
    }

    uint32_t width = 0, height = 0;
    uint16_t samples = 1, bits = 1, format = SAMPLEFORMAT_UINT, planar = PLANARCONFIG_CONTIG, photometric = 0;

    TIFFGetField(tif, TIFFTAG_IMAGEWIDTH, &width);
    TIFFGetField(tif, TIFFTAG_IMAGELENGTH, &height);
    TIFFGetFieldDefaulted(tif, TIFFTAG_SAMPLESPERPIXEL, &samples);
    TIFFGetFieldDefaulted(tif, TIFFTAG_BITSPERSAMPLE, &bits);
    TIFFGetFieldDefaulted(tif, TIFFTAG_SAMPLEFORMAT, &format);
    TIFFGetFieldDefaulted(tif, TIFFTAG_PLANARCONFIG, &planar);
    TIFFGetField(tif, TIFFTAG_PHOTOMETRIC, &photometric);

    if (width == 0 || height == 0) {
        set_error("Invalid TIFF dimensions: %ux%u", width, height);
        TIFFClose(tif);
        return false;
    }

    const bool separate = planar == PLANARCONFIG_SEPARATE;
    const uint16_t planes = separate ? samples : 1;
    const uint16_t perPlane = separate ? 1 : samples;
    const uint64_t sizeCeiling = static_cast<uint64_t>(1) << 48; // 256 TB

    uint64_t rowBits = 0, planeBytes = 0, totalBytes = 0;

    if (mul_overflows(width, static_cast<uint64_t>(bits) * perPlane, &rowBits)) {
        set_error("Image too large to unpack: %ux%u at %u-bit x%u", width, height, bits, samples);
        TIFFClose(tif);
        return false;
    }

    // One row of one plane, at the file's own packing.
    const uint64_t rowBytes = (rowBits + 7) / 8;

    if (mul_overflows(rowBytes, height, &planeBytes) || mul_overflows(planeBytes, planes, &totalBytes) ||
        planeBytes == 0 || totalBytes > sizeCeiling || !fits_allocation(totalBytes)) {
        set_error("Image too large to unpack: %ux%u at %u-bit x%u", width, height, bits, samples);
        TIFFClose(tif);
        return false;
    }

    // The whole image at native packing. Whole-image by design - see the header.
    auto *native = static_cast<uint8_t *>(std::calloc(static_cast<size_t>(totalBytes), 1));
    if (!native) {
        set_error("Failed to allocate %llu bytes.", (unsigned long long) totalBytes);
        TIFFClose(tif);
        return false;
    }

    bool ok = true;

    if (TIFFIsTiled(tif)) {
        uint32_t tileWidth = 0, tileHeight = 0;
        TIFFGetField(tif, TIFFTAG_TILEWIDTH, &tileWidth);
        TIFFGetField(tif, TIFFTAG_TILELENGTH, &tileHeight);

        const tmsize_t tileBytes = TIFFTileSize(tif);
        auto *tile = static_cast<uint8_t *>(std::malloc(static_cast<size_t>(tileBytes)));

        if (!tile || tileWidth == 0 || tileHeight == 0) {
            set_error("Could not set up a %lld byte tile buffer.", (long long) tileBytes);
            std::free(tile);
            ok = false;
        } else {
            const uint64_t tileRowBytes = (static_cast<uint64_t>(tileWidth) * bits * perPlane + 7) / 8;

            for (uint16_t p = 0; p < planes && ok; p++)
            for (uint32_t ty = 0; ty < height && ok; ty += tileHeight)
            for (uint32_t tx = 0; tx < width && ok; tx += tileWidth) {
                if (TIFFReadEncodedTile(tif, TIFFComputeTile(tif, tx, ty, 0, p), tile, tileBytes) < 0) {
                    if (last_tiff_error[0] == '\0')
                        set_error("Failed to read the tile at %u,%u.", tx, ty);
                    ok = false;
                    break;
                }

                const uint32_t rows = (ty + tileHeight < height) ? tileHeight : height - ty;\
                const uint64_t offsetBytes = (static_cast<uint64_t>(tx) * bits * perPlane) / 8;
                const uint64_t copyBytes = std::min<uint64_t>(tileRowBytes, rowBytes - offsetBytes);

                for (uint32_t r = 0; r < rows; r++)
                    std::memcpy(native + p * planeBytes + (ty + r) * rowBytes + offsetBytes, tile + r * tileRowBytes, static_cast<size_t>(copyBytes));
            }

            std::free(tile);
        }
    } else {
        uint32_t rowsPerStrip = height;
        TIFFGetFieldDefaulted(tif, TIFFTAG_ROWSPERSTRIP, &rowsPerStrip);

        if (rowsPerStrip == 0 || rowsPerStrip > height)
            rowsPerStrip = height;

        const tmsize_t stripBytes = TIFFStripSize(tif);
        auto *strip = static_cast<uint8_t *>(std::malloc(static_cast<size_t>(stripBytes)));

        if (!strip) {
            set_error("Failed to allocate a %lld byte strip buffer.", (long long) stripBytes);
            ok = false;
        } else {
            for (uint16_t p = 0; p < planes && ok; p++)
            for (uint32_t y = 0; y < height && ok; y += rowsPerStrip) {
                const tmsize_t read = read_strip(tif, TIFFComputeStrip(tif, y, p), strip, stripBytes, bits);

                if (read < 0) {
                    if (last_tiff_error[0] == '\0')
                        set_error("Failed to read the strip at row %u.", y);

                    ok = false;
                    break;
                }

                const uint32_t rows = (y + rowsPerStrip < height) ? rowsPerStrip : height - y;
                const uint64_t want = std::min<uint64_t>(static_cast<uint64_t>(read), rowBytes * rows);

                std::memcpy(native + p * planeBytes + static_cast<uint64_t>(y) * rowBytes, strip, static_cast<size_t>(want));
            }

            std::free(strip);
        }
    }

    TIFFClose(tif);

    if (!ok) {
        std::free(native);
        return false;
    }

    const bool invert = photometric == PHOTOMETRIC_MINISWHITE;
    const bool floatOut = output_kind == TIFF_OUT_RGBA_F32;
    const uint32_t outSamples = output_kind == TIFF_OUT_GRAY8 ? 1 : 4;
    const uint32_t outBytes = floatOut ? 4 : 1;
    const uint64_t outStride = static_cast<uint64_t>(width) * outSamples * outBytes;

    uint64_t outTotal = 0;
    if (mul_overflows(outStride, height, &outTotal) || outTotal > sizeCeiling || !fits_allocation(outTotal) || outStride > UINT32_MAX) {
        set_error("Converted image too large: %ux%u", width, height);
        std::free(native);
        return false;
    }

    auto *out = static_cast<uint8_t *>(std::malloc(static_cast<size_t>(outTotal)));
    if (!out) {
        set_error("Failed to allocate %llu bytes for the converted image.", (unsigned long long) outTotal);
        std::free(native);
        return false;
    }

    for (uint32_t y = 0; y < height; y++) {
        auto *dst = out + static_cast<uint64_t>(y) * outStride;

        for (uint32_t x = 0; x < width; x++) {
            float v[4] = {0, 0, 0, 1};

            for (uint16_t sIdx = 0; sIdx < samples && sIdx < 4; sIdx++) {
                const uint8_t *row = separate
                    ? native + static_cast<uint64_t>(sIdx) * planeBytes + static_cast<uint64_t>(y) * rowBytes
                    : native + static_cast<uint64_t>(y) * rowBytes;

                const uint64_t index = separate ? x : static_cast<uint64_t>(x) * samples + sIdx;

                v[sIdx] = read_sample(row, index, bits, format);
            }

            // One sample means grey: spread it, so an RGBA caller does not get a red image.
            if (samples < 3) {
                v[3] = samples == 2 ? v[1] : 1.0f;
                v[1] = v[0];
                v[2] = v[0];
            } else if (samples == 3) {
                v[3] = 1.0f;
            }

            // After the spread, so a grey-plus-alpha MINISWHITE image inverts its grey and not
            // its transparency.
            if (invert)
                for (int i = 0; i < 3; i++) v[i] = 1.0f - v[i];

            if (floatOut) {
                auto *f = reinterpret_cast<float *>(dst) + static_cast<uint64_t>(x) * 4;
                f[0] = v[0]; f[1] = v[1]; f[2] = v[2]; f[3] = v[3];
            } else if (output_kind == TIFF_OUT_GRAY8) {
                const float g = v[0] < 0 ? 0 : (v[0] > 1 ? 1 : v[0]);
                dst[x] = static_cast<uint8_t>(g * 255.0f + 0.5f);
            } else {
                auto *b = dst + static_cast<uint64_t>(x) * 4;
                for (int i = 0; i < 4; i++) {
                    const float c = v[i] < 0 ? 0 : (v[i] > 1 ? 1 : v[i]);
                    b[i] = static_cast<uint8_t>(c * 255.0f + 0.5f);
                }
            }
        }
    }

    std::free(native);

    *out_pixels = out;
    *out_width = static_cast<int>(width);
    *out_height = static_cast<int>(height);
    *out_stride = static_cast<uint32_t>(outStride);
    return true;
}

TIFF_API void free_tiff_pixels(uint8_t *ptr) {
    if (ptr)
        std::free(ptr);
}

} // extern "C"
