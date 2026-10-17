#include "raw_native.h"

#include <algorithm>
#include <atomic>
#include <cstdarg>
#include <cstdio>
#include <cstring>
#include <memory>
#include <new>

#include <libraw/libraw.h>

#ifdef _WIN32
#include <filesystem>
#endif

#ifdef __clang__
#define THREAD_LOCAL __thread
#else
#define THREAD_LOCAL thread_local
#endif

struct raw_image {
    LibRaw processor;
    std::atomic<bool> cancelled{false};
    std::atomic<int> damage{RAW_DAMAGE_NONE};
    std::atomic<int32_t> stage{RAW_STAGE_IDLE};
    bool developed = false;
};

static THREAD_LOCAL char error_message[512] = "";

static void set_error(const char *fmt, ...) {
    va_list args;
    va_start(args, fmt);
    std::vsnprintf(error_message, sizeof(error_message), fmt, args);
    va_end(args);
}

static void clear_error() { error_message[0] = '\0'; }

// Whether a cancel at this point would land only about when the develop ends anyway.
static bool is_finishing(const LibRaw &processor, LibRaw_progress stage, int iter, int expect) {
    if (stage >= LIBRAW_PROGRESS_THUMB_LOAD)
        return false;

    if (stage > LIBRAW_PROGRESS_INTERPOLATE)
        return true;

    // pre_interpolate's last callback; next is the demosaic, and xtrans_interpolate never calls back.
    const auto &p = processor.imgdata;
    return stage == LIBRAW_PROGRESS_PRE_INTERPOLATE && iter == expect - 1 && p.idata.filters == 9 &&
           (p.params.user_qual < 0 || p.params.user_qual >= 2);
}

static int on_progress(void *data, enum LibRaw_progress stage, int iter, int expect) {
    auto *image = static_cast<raw_image *>(data);
    if (is_finishing(image->processor, stage, iter, expect))
        image->stage.store(RAW_STAGE_FINISHING, std::memory_order_relaxed);

    return image->cancelled.load(std::memory_order_relaxed) ? 1 : 0;
}

static void on_data_error(void *data, const char *, const INT64 offset) {
    int expected = RAW_DAMAGE_NONE;
    static_cast<raw_image *>(data)->damage.compare_exchange_strong(expected, offset < 0 ? RAW_DAMAGE_TRUNCATED
                                                                                        : RAW_DAMAGE_CORRUPT);
}

static void copy_string(char *dst, size_t capacity, const char *src) {
    std::snprintf(dst, capacity, "%s", src ? src : "");
}

// Thumbnail formats make_mem_thumb turns into JPEG or 8-bit RGB.
static int32_t preview_format_of(LibRaw_internal_thumbnail_formats format) {
    switch (format) {
        case LIBRAW_INTERNAL_THUMBNAIL_JPEG:
            return RAW_PREVIEW_JPEG;
        case LIBRAW_INTERNAL_THUMBNAIL_PPM:
        case LIBRAW_INTERNAL_THUMBNAIL_DNG_YCBCR:
        case LIBRAW_INTERNAL_THUMBNAIL_KODAK_THUMB:
        case LIBRAW_INTERNAL_THUMBNAIL_KODAK_YCBCR:
        case LIBRAW_INTERNAL_THUMBNAIL_KODAK_RGB:
        case LIBRAW_INTERNAL_THUMBNAIL_LAYER:
        case LIBRAW_INTERNAL_THUMBNAIL_ROLLEI:
            return RAW_PREVIEW_RGB8;
        default:
            return RAW_PREVIEW_NONE;
    }
}

// How many previews LibRaw lists, within its own array.
static int listed_previews(const LibRaw &processor) {
    const auto &previews = processor.imgdata.thumbs_list;
    return previews.thumbcount < LIBRAW_THUMBNAIL_MAXCOUNT ? previews.thumbcount : LIBRAW_THUMBNAIL_MAXCOUNT;
}

static int32_t sensor_of(LibRaw &processor) {
    const auto &id = processor.imgdata.idata;

    if (id.is_foveon)
        return RAW_SENSOR_OTHER;
    if (id.filters == 9)
        return RAW_SENSOR_XTRANS;
    if (id.filters == 0)
        return id.colors == 1 ? RAW_SENSOR_MONOCHROME : RAW_SENSOR_LINEAR;
    if (id.filters < 1000)
        return RAW_SENSOR_OTHER;

    return RAW_SENSOR_BAYER;
}

static void fill_info(LibRaw &processor, raw_info *info) {
    const auto &d = processor.imgdata;
    std::memset(info, 0, sizeof(*info));

    int width = d.sizes.width;
    int height = d.sizes.height;
    if (d.sizes.pixel_aspect > 0.0 && d.sizes.pixel_aspect != 1.0) {
        if (d.sizes.pixel_aspect < 1.0)
            height = static_cast<int>(height / d.sizes.pixel_aspect + 0.5);
        else
            width = static_cast<int>(width * d.sizes.pixel_aspect + 0.5);
    }

    const bool swaps = (d.sizes.flip & 4) != 0;
    info->width = swaps ? height : width;
    info->height = swaps ? width : height;
    info->raw_width = d.sizes.raw_width;
    info->raw_height = d.sizes.raw_height;
    info->flip = d.sizes.flip;

    info->sensor = sensor_of(processor);
    if (info->sensor == RAW_SENSOR_BAYER) {
        for (int i = 0; i < 4; i++) {
            const int color = processor.COLOR(i / 2, i % 2);
            info->cfa_pattern[i] = color >= 0 && color < 4 ? d.idata.cdesc[color] : '?';
        }
    }

    info->raw_bits = static_cast<int32_t>(d.color.raw_bps);

    for (int i = 0; i < 4; i++)
        info->as_shot_multipliers[i] = d.color.cam_mul[i];

    info->dng_version = d.idata.dng_version;
    info->preview_color_space = static_cast<int32_t>(d.color.dng_levels.preview_colorspace);
    if (d.idata.dng_version && d.color.dng_levels.baseline_exposure > -100.f)
        info->baseline_exposure = d.color.dng_levels.baseline_exposure;

    const auto &previews = d.thumbs_list;
    const int count = listed_previews(processor);
    info->preview_count = count;

    for (int i = 0; i < count; i++) {
        const auto &item = previews.thumblist[i];
        const int32_t format = preview_format_of(item.tformat);
        if (format == RAW_PREVIEW_NONE)
            continue;

        if (static_cast<int64_t>(item.twidth) * item.theight >
            static_cast<int64_t>(info->preview_width) * info->preview_height) {
            info->preview_width = item.twidth;
            info->preview_height = item.theight;
            info->preview_format = format;
        }
    }

    copy_string(info->make, sizeof(info->make), d.idata.normalized_make[0] ? d.idata.normalized_make : d.idata.make);
    copy_string(info->model, sizeof(info->model), d.idata.normalized_model[0] ? d.idata.normalized_model : d.idata.model);

    info->iso_speed = d.other.iso_speed;
    info->shutter = d.other.shutter;
    info->aperture = d.other.aperture;
    info->focal_length = d.other.focal_len;
    info->timestamp = static_cast<int64_t>(d.other.timestamp);
    copy_string(info->lens, sizeof(info->lens), d.lens.Lens);
}

// Unpacks preview index into out; false with the LibRaw code in last when it cannot be had.
static bool extract_one(LibRaw &processor, int index, raw_preview *out, int *last) {
    *last = processor.unpack_thumb_ex(index);
    if (*last != LIBRAW_SUCCESS)
        return false;

    int error = 0;
    libraw_processed_image_t *thumb = processor.dcraw_make_mem_thumb(&error);
    if (!thumb) {
        *last = error;
        return false;
    }

    if (thumb->type == LIBRAW_IMAGE_JPEG && thumb->data_size > 0) {
        out->format = RAW_PREVIEW_JPEG;
        out->width = processor.imgdata.thumbnail.twidth;
        out->height = processor.imgdata.thumbnail.theight;
    } else if (thumb->type == LIBRAW_IMAGE_BITMAP && thumb->bits == 8 && thumb->colors == 3 &&
               static_cast<size_t>(thumb->width) * thumb->height * 3 <= thumb->data_size) {
        out->format = RAW_PREVIEW_RGB8;
        out->width = thumb->width;
        out->height = thumb->height;
    } else {
        LibRaw::dcraw_clear_mem(thumb);
        *last = LIBRAW_UNSUPPORTED_THUMBNAIL;
        return false;
    }

    out->data = thumb->data;
    out->size = thumb->data_size;
    out->owner = thumb;
    return true;
}

template<typename Open>
static raw_image *open_with(raw_info *out_info, Open open) {
    clear_error();
    if (out_info)
        std::memset(out_info, 0, sizeof(*out_info));

    try {
        std::unique_ptr<raw_image> image(new (std::nothrow) raw_image());
        if (!image) {
            set_error("Out of memory creating the LibRaw processor.");
            return nullptr;
        }

        image->processor.set_progress_handler(on_progress, image.get());
        image->processor.set_dataerror_handler(on_data_error, image.get());

        const int result = open(image->processor);
        if (result != LIBRAW_SUCCESS) {
            set_error("LibRaw could not open the file: %s", libraw_strerror(result));
            return nullptr;
        }

        if (out_info)
            fill_info(image->processor, out_info);

        return image.release();
    } catch (const std::exception &e) {
        set_error("LibRaw open failed: %s", e.what());
    } catch (...) {
        set_error("LibRaw open failed.");
    }

    if (out_info)
        std::memset(out_info, 0, sizeof(*out_info));
    return nullptr;
}

extern "C" {

RAW_NATIVE_API const char *get_last_raw_error(void) { return error_message; }

RAW_NATIVE_API const char *raw_libraw_version(void) { return libraw_version(); }

RAW_NATIVE_API raw_image *raw_open_memory(const uint8_t *data, size_t size, raw_info *out_info) {
    if (!data || size == 0) {
        set_error("No data.");
        if (out_info)
            std::memset(out_info, 0, sizeof(*out_info));
        return nullptr;
    }

    return open_with(out_info, [&](LibRaw &processor) { return processor.open_buffer(data, size); });
}

RAW_NATIVE_API raw_image *raw_open_file(const char *utf8_path, raw_info *out_info) {
    if (!utf8_path) {
        set_error("No path.");
        if (out_info)
            std::memset(out_info, 0, sizeof(*out_info));
        return nullptr;
    }

    return open_with(out_info, [&](LibRaw &processor) {
#ifdef _WIN32
        return processor.open_file(std::filesystem::u8path(utf8_path).wstring().c_str());
#else
        return processor.open_file(utf8_path);
#endif
    });
}

RAW_NATIVE_API bool raw_extract_preview(raw_image *image, int32_t min_long_side, raw_preview *out_preview) {
    clear_error();
    if (out_preview)
        std::memset(out_preview, 0, sizeof(*out_preview));

    if (!image || !out_preview) {
        set_error("Invalid arguments.");
        return false;
    }

    try {
        auto &processor = image->processor;
        const auto &previews = processor.imgdata.thumbs_list;
        const int count = listed_previews(processor);

        int order[LIBRAW_THUMBNAIL_MAXCOUNT];
        int candidates = 0;
        for (int i = 0; i < count; i++) {
            if (preview_format_of(previews.thumblist[i].tformat) != RAW_PREVIEW_NONE)
                order[candidates++] = i;
        }

        auto long_side = [&](int i) {
            const auto &item = previews.thumblist[i];
            return item.twidth > item.theight ? item.twidth : item.theight;
        };

        // Smallest that is big enough first, then the rest largest first, so a preview that
        // fails to unpack falls back to the next best one.
        std::stable_sort(order, order + candidates, [&](int a, int b) {
            const int la = long_side(a);
            const int lb = long_side(b);
            const bool a_fits = la >= min_long_side;
            const bool b_fits = lb >= min_long_side;
            if (a_fits != b_fits)
                return a_fits;
            return a_fits ? la < lb : la > lb;
        });

        int last = LIBRAW_NO_THUMBNAIL;
        for (int c = 0; c < candidates; c++) {
            if (extract_one(processor, order[c], out_preview, &last))
                return true;
        }

        set_error("No usable embedded preview: %s", libraw_strerror(last));
    } catch (const std::exception &e) {
        set_error("Preview extraction failed: %s", e.what());
    } catch (...) {
        set_error("Preview extraction failed.");
    }

    std::memset(out_preview, 0, sizeof(*out_preview));
    return false;
}

RAW_NATIVE_API int32_t raw_list_previews(raw_image *image, raw_preview_entry *out_entries, int32_t capacity) {
    clear_error();
    if (!image || !out_entries || capacity <= 0)
        return 0;

    try {
        const auto &previews = image->processor.imgdata.thumbs_list;
        const int count = listed_previews(image->processor);

        int32_t written = 0;
        for (int i = 0; i < count && written < capacity; i++) {
            const auto &item = previews.thumblist[i];
            const int32_t format = preview_format_of(item.tformat);
            if (format == RAW_PREVIEW_NONE || item.twidth == 0 || item.theight == 0)
                continue;

            // Some formats list the same stream twice.
            bool seen = false;
            for (int j = 0; j < i && !seen; j++) {
                const auto &other = previews.thumblist[j];
                seen = other.toffset == item.toffset && other.tlength == item.tlength;
            }
            if (seen)
                continue;

            out_entries[written++] = raw_preview_entry{i, format, item.twidth, item.theight,
                                                       static_cast<int64_t>(item.tlength)};
        }

        // Largest first: insertion sort over at most LIBRAW_THUMBNAIL_MAXCOUNT entries.
        for (int32_t a = 1; a < written; a++) {
            const raw_preview_entry entry = out_entries[a];
            const int64_t area = static_cast<int64_t>(entry.width) * entry.height;
            int32_t b = a - 1;
            while (b >= 0 && static_cast<int64_t>(out_entries[b].width) * out_entries[b].height < area) {
                out_entries[b + 1] = out_entries[b];
                b--;
            }
            out_entries[b + 1] = entry;
        }

        return written;
    } catch (...) {
        set_error("Listing the previews failed.");
        return 0;
    }
}

RAW_NATIVE_API bool raw_extract_preview_at(raw_image *image, int32_t index, raw_preview *out_preview) {
    clear_error();
    if (out_preview)
        std::memset(out_preview, 0, sizeof(*out_preview));

    if (!image || !out_preview || index < 0 || index >= listed_previews(image->processor)) {
        set_error("Invalid arguments.");
        return false;
    }

    try {
        int last = LIBRAW_NO_THUMBNAIL;
        if (extract_one(image->processor, index, out_preview, &last))
            return true;

        set_error("The embedded preview could not be read: %s", libraw_strerror(last));
    } catch (const std::exception &e) {
        set_error("Preview extraction failed: %s", e.what());
    } catch (...) {
        set_error("Preview extraction failed.");
    }

    std::memset(out_preview, 0, sizeof(*out_preview));
    return false;
}

RAW_NATIVE_API void free_raw_preview(void *owner) {
    if (owner)
        LibRaw::dcraw_clear_mem(static_cast<libraw_processed_image_t *>(owner));
}

RAW_NATIVE_API bool raw_develop(raw_image *image, const raw_develop_options *options, raw_pixels *out_pixels) {
    clear_error();
    if (out_pixels)
        std::memset(out_pixels, 0, sizeof(*out_pixels));

    if (!image || !options || !out_pixels) {
        set_error("Invalid arguments.");
        return false;
    }

    if (image->developed) {
        set_error("The image was already developed.");
        return false;
    }
    image->developed = true;
    image->stage.store(RAW_STAGE_EARLY, std::memory_order_relaxed);

    try {
        auto &processor = image->processor;
        auto &params = processor.imgdata.params;

        // Linear, scene-referred light: no curve, no auto exposure, camera white balance.
        params.output_bps = 16;
        params.gamm[0] = 1.0;
        params.gamm[1] = 1.0;
        params.no_auto_bright = 1;
        params.use_camera_wb = 1;
        params.output_color = 1; // sRGB primaries
        params.highlight = 0;    // clip at saturation
        params.half_size = options->half_size ? 1 : 0;
        params.user_qual = options->demosaic;

        int result = processor.unpack();
        if (result != LIBRAW_SUCCESS) {
            set_error("Could not unpack the raw data: %s", libraw_strerror(result));
            return false;
        }

        // Read before processing, which subtracts black and zeroes it.
        // Black is a base, plus one per channel, plus a repeating pattern (cblack[4] x cblack[5] at cblack[6]).
        const auto &color = processor.imgdata.color;
        unsigned channel_min = color.cblack[0], channel_max = color.cblack[0];
        for (int c = 1; c < 4; c++) {
            channel_min = color.cblack[c] < channel_min ? color.cblack[c] : channel_min;
            channel_max = color.cblack[c] > channel_max ? color.cblack[c] : channel_max;
        }

        unsigned pattern_min = 0, pattern_max = 0;
        const unsigned cells = color.cblack[4] * color.cblack[5];
        if (cells > 0 && cells <= LIBRAW_CBLACK_SIZE - 6) {
            pattern_min = pattern_max = color.cblack[6];
            for (unsigned i = 1; i < cells; i++) {
                pattern_min = color.cblack[6 + i] < pattern_min ? color.cblack[6 + i] : pattern_min;
                pattern_max = color.cblack[6 + i] > pattern_max ? color.cblack[6 + i] : pattern_max;
            }
        }

        const int black_min = static_cast<int>(color.black + channel_min + pattern_min);
        const int black_max = static_cast<int>(color.black + channel_max + pattern_max);
        const int white = static_cast<int>(color.maximum);

        result = processor.dcraw_process();
        if (result != LIBRAW_SUCCESS) {
            set_error("Could not develop the raw data: %s", libraw_strerror(result));
            return false;
        }
        image->stage.store(RAW_STAGE_FINISHING, std::memory_order_relaxed);

        int error = 0;
        libraw_processed_image_t *developed = processor.dcraw_make_mem_image(&error);
        if (!developed) {
            set_error("Could not produce the developed image: %s", libraw_strerror(error));
            return false;
        }

        if (developed->type != LIBRAW_IMAGE_BITMAP || developed->bits != 16 || developed->colors != 3 ||
            static_cast<size_t>(developed->width) * developed->height * 3 * sizeof(uint16_t) > developed->data_size) {
            LibRaw::dcraw_clear_mem(developed);
            set_error("LibRaw produced an unexpected image layout.");
            return false;
        }

        // The working buffers are several times the result; drop them before returning.
        processor.recycle();

        out_pixels->width = developed->width;
        out_pixels->height = developed->height;
        out_pixels->pixels = reinterpret_cast<const uint16_t *>(developed->data);
        out_pixels->owner = developed;
        out_pixels->black_level_min = black_min;
        out_pixels->black_level_max = black_max;
        out_pixels->white_level = white;
        out_pixels->damage = image->damage.load();
        return true;
    } catch (const std::exception &e) {
        set_error("Raw development failed: %s", e.what());
    } catch (...) {
        set_error("Raw development failed.");
    }

    std::memset(out_pixels, 0, sizeof(*out_pixels));
    return false;
}

RAW_NATIVE_API void free_raw_pixels(void *owner) {
    if (owner)
        LibRaw::dcraw_clear_mem(static_cast<libraw_processed_image_t *>(owner));
}

RAW_NATIVE_API void raw_cancel(raw_image *image) {
    if (!image)
        return;

    image->cancelled.store(true, std::memory_order_relaxed);
    // The progress callback only runs between stages; decoders check this flag row by row.
    image->processor.setCancelFlag();
}

RAW_NATIVE_API int32_t raw_develop_stage(raw_image *image) {
    return image ? image->stage.load(std::memory_order_relaxed) : RAW_STAGE_IDLE;
}

RAW_NATIVE_API void raw_close(raw_image *image) {
    try {
        delete image;
    } catch (...) {
    }
}

} // extern "C"
