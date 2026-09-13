#include "j2k_native.h"

#include <openjpeg.h>

#include <climits>
#include <cstdarg>
#include <cstdint>
#include <cstdio>
#include <cstdlib>
#include <cstring>
#include <mutex>
#include <unordered_set>

#ifdef __clang__
#define THREAD_LOCAL __thread
#else
#define THREAD_LOCAL thread_local
#endif

static THREAD_LOCAL char last_j2k_error[512] = "";

static std::unordered_set<void *> j2k_allocated_ptrs;
static std::mutex j2k_alloc_mutex;

static void set_error(const char *fmt, ...) {
    va_list args;
    va_start(args, fmt);
    std::vsnprintf(last_j2k_error, sizeof(last_j2k_error), fmt, args);
    va_end(args);
}

static void clear_error() { last_j2k_error[0] = '\0'; }

static void set_fallback_error(const char *msg) {
    if (last_j2k_error[0] == '\0')
        set_error("%s", msg);
}

static void opj_error_callback(const char *msg, void * /*client_data*/) {
    if (!msg)
        return;

    int written = std::snprintf(last_j2k_error, sizeof(last_j2k_error), "OpenJPEG: %s", msg);
    if (written < 0)
        return;

    if (written > (int) sizeof(last_j2k_error) - 1)
        written = (int) sizeof(last_j2k_error) - 1;

    while (written > 0 && (last_j2k_error[written - 1] == '\n' || last_j2k_error[written - 1] == '\r' || last_j2k_error[written - 1] == ' '))
        last_j2k_error[--written] = '\0';
}
static void opj_warning_callback(const char * /*msg*/, void * /*client_data*/) {}
static void opj_info_callback(const char * /*msg*/, void * /*client_data*/) {}

static bool is_jp2(const uint8_t *data, size_t size) {
    // JP2 signature box: 0x0000000C 0x6A502020 0x0D0A870A
    // bytes: 00 00 00 0C 6A 50 20 20 0D 0A 87 0A
    if (size < 12)
        return false;
    return data[0] == 0x00 && data[1] == 0x00 && data[2] == 0x00 && data[3] == 0x0C && data[4] == 0x6A &&
           data[5] == 0x50 && data[6] == 0x20 && data[7] == 0x20 && data[8] == 0x0D && data[9] == 0x0A &&
           data[10] == 0x87 && data[11] == 0x0A;
}

static bool is_j2k_codestream(const uint8_t *data, size_t size) {
    // SOC marker: FF 4F
    if (size < 2)
        return false;

    return data[0] == 0xFF && data[1] == 0x4F;
}

struct MemStream {
    const uint8_t *data = nullptr;
    size_t size = 0;
    size_t pos = 0;
};

static OPJ_SIZE_T mem_read(void *p_buffer, OPJ_SIZE_T p_nb_bytes, void *p_user_data) {
    auto *ms = reinterpret_cast<MemStream *>(p_user_data);
    if (!ms || !ms->data)
        return (OPJ_SIZE_T) -1;

    const size_t remaining = ms->pos < ms->size ? ms->size - ms->pos : 0;

    if (remaining == 0)
        return (OPJ_SIZE_T) -1;

    size_t to_read = (size_t) p_nb_bytes;
    if (to_read > remaining)
        to_read = remaining;

    std::memcpy(p_buffer, ms->data + ms->pos, to_read);
    ms->pos += to_read;
    return (OPJ_SIZE_T) to_read;
}

static OPJ_OFF_T mem_skip(OPJ_OFF_T p_nb_bytes, void *p_user_data) {
    auto *ms = reinterpret_cast<MemStream *>(p_user_data);
    if (!ms || p_nb_bytes < 0)
        return (OPJ_OFF_T) -1;

    if (p_nb_bytes == 0)
        return 0;

    const size_t remaining = ms->pos < ms->size ? ms->size - ms->pos : 0;
    if ((uint64_t) p_nb_bytes > (uint64_t) remaining)
        return (OPJ_OFF_T) -1;

    ms->pos += (size_t) p_nb_bytes;
    return p_nb_bytes;
}

static OPJ_BOOL mem_seek(OPJ_OFF_T p_nb_bytes, void *p_user_data) {
    auto *ms = reinterpret_cast<MemStream *>(p_user_data);
    if (!ms || p_nb_bytes < 0)
        return OPJ_FALSE;

    const uint64_t new_pos = (uint64_t) p_nb_bytes;
    if (new_pos > (uint64_t) ms->size)
        return OPJ_FALSE;

    ms->pos = (size_t) new_pos;
    return OPJ_TRUE;
}

static void mem_free(void * /*p_user_data*/) {
    // We own nothing here.
}

static opj_stream_t *create_mem_stream(MemStream *ms) {
    const OPJ_SIZE_T bufSize = 64 * 1024;

    opj_stream_t *stream = opj_stream_create(bufSize, OPJ_TRUE);
    if (!stream)
        return nullptr;

    opj_stream_set_user_data(stream, ms, mem_free);
    opj_stream_set_user_data_length(stream, ms->size);

    opj_stream_set_read_function(stream, mem_read);
    opj_stream_set_skip_function(stream, mem_skip);
    opj_stream_set_seek_function(stream, mem_seek);

    return stream;
}

struct OpjSession {
    opj_codec_t *codec = nullptr;
    opj_stream_t *stream = nullptr;
    opj_image_t *image = nullptr;

    OpjSession() = default;
    OpjSession(const OpjSession &) = delete;
    OpjSession &operator=(const OpjSession &) = delete;

    ~OpjSession() {
        if (image)
            opj_image_destroy(image);

        if (stream)
            opj_stream_destroy(stream);

        if (codec)
            opj_destroy_codec(codec);
    }
};

static inline int clamp_int(int v, int lo, int hi) {
    if (v < lo)
        return lo;
    if (v > hi)
        return hi;
    return v;
}

struct ScaleParams {
    int prec = 8;
    bool sgnd = false;
    int maxv = 255;
};

static inline ScaleParams make_scale_params(const opj_image_comp_t &comp) {
    ScaleParams p;
    p.prec = (int) comp.prec;
    p.sgnd = (comp.sgnd != 0);
    if (p.prec <= 0) {
        p.prec = 8;
        p.maxv = 255;
    } else if (p.prec >= 31) {
        p.prec = 30;
        p.maxv = (1 << 30) - 1;
    } else {
        p.maxv = (1 << p.prec) - 1;
        if (p.maxv <= 0)
            p.maxv = 255;
    }

    return p;
}

static inline uint8_t scale_to_u8(int sample, const ScaleParams &p) {
    int v = sample;
    if (p.sgnd) {
        v = v + (1 << (p.prec - 1));
    }

    int out = (v * 255 + p.maxv / 2) / p.maxv;
    if (out < 0)
        out = 0;

    if (out > 255)
        out = 255;

    return (uint8_t) out;
}

enum class ColorTransform {
    None, // already RGB (or gray broadcast into RGB)
    Ycc,  // sYCC and eYCC: luma plus two offset-binary chroma channels
    Cmyk, // subtractive ink, with the fourth component as black rather than alpha
};

struct Plane {
    const opj_image_comp_t *comp = nullptr;
    ScaleParams scale;
};

static bool is_full_res(const opj_image_comp_t &c, int w, int h) {
    return c.dx == 1 && c.dy == 1 && c.w == (OPJ_UINT32) w && c.h == (OPJ_UINT32) h;
}

static inline uint8_t sample_plane(const Plane &plane, int x, int y) {
    const opj_image_comp_t &comp = *plane.comp;
    const int dx = (comp.dx > 0) ? (int) comp.dx : 1;
    const int dy = (comp.dy > 0) ? (int) comp.dy : 1;

    const int cx = clamp_int(x / dx, 0, (int) comp.w - 1);
    const int cy = clamp_int(y / dy, 0, (int) comp.h - 1);

    return scale_to_u8(comp.data[(size_t) cy * (size_t) comp.w + (size_t) cx], plane.scale);
}

static inline uint8_t clamp_u8(int v) {
    if (v < 0)
        return 0;
    if (v > 255)
        return 255;
    return (uint8_t) v;
}

static inline void to_rgb(ColorTransform transform, uint8_t c0, uint8_t c1, uint8_t c2, uint8_t c3, uint8_t *r,
                          uint8_t *g, uint8_t *b) {
    switch (transform) {
        case ColorTransform::Ycc: {
            // sYCC, the full-range Rec.601 matrix (IEC 61966-2-1 / ITU-T T.871), with chroma
            // taken off its 128 midpoint. eYCC's published coefficients differ from these in the
            // fourth decimal place - under a tenth of an 8-bit level - so it shares the path.
            const float y = (float) c0;
            const float cb = (float) c1 - 128.0f;
            const float cr = (float) c2 - 128.0f;

            *r = clamp_u8((int) (y + 1.402f * cr + 0.5f));
            *g = clamp_u8((int) (y - 0.344136f * cb - 0.714136f * cr + 0.5f));
            *b = clamp_u8((int) (y + 1.772f * cb + 0.5f));
            break;
        }
        case ColorTransform::Cmyk: {
            // Ink coverage, so every channel is inverted: none of a colorant means all of the
            // light. Black multiplies the other three rather than being a fourth output.
            const int k = 255 - (int) c3;

            *r = (uint8_t) (((255 - (int) c0) * k + 127) / 255);
            *g = (uint8_t) (((255 - (int) c1) * k + 127) / 255);
            *b = (uint8_t) (((255 - (int) c2) * k + 127) / 255);
            break;
        }
        case ColorTransform::None:
        default:
            *r = c0;
            *g = c1;
            *b = c2;
            break;
    }
}

static void interleave_rgba(const Plane planes[4], ColorTransform transform, bool has_alpha, bool fast_path,
                            uint8_t *buffer, int stride, int w, int h) {

    const bool broadcast = planes[0].comp == planes[1].comp && planes[1].comp == planes[2].comp;
    const bool has_fourth = planes[3].comp != nullptr;

    for (int y = 0; y < h; ++y) {
        uint8_t *out = buffer + (size_t) y * (size_t) stride;

        if (fast_path) {
            const int *rows[4] = {nullptr, nullptr, nullptr, nullptr};
            for (int c = 0; c < 4; ++c) {
                if (planes[c].comp)
                    rows[c] = planes[c].comp->data + (size_t) y * (size_t) planes[c].comp->w;
            }

            for (int x = 0; x < w; ++x) {
                const uint8_t c0 = scale_to_u8(rows[0][x], planes[0].scale);
                const uint8_t c1 = broadcast ? c0 : scale_to_u8(rows[1][x], planes[1].scale);
                const uint8_t c2 = broadcast ? c0 : scale_to_u8(rows[2][x], planes[2].scale);
                const uint8_t c3 = has_fourth ? scale_to_u8(rows[3][x], planes[3].scale) : 255;

                to_rgb(transform, c0, c1, c2, c3, &out[x * 4 + 0], &out[x * 4 + 1], &out[x * 4 + 2]);
                out[x * 4 + 3] = has_alpha ? c3 : 255;
            }
        } else {
            for (int x = 0; x < w; ++x) {
                const uint8_t c0 = sample_plane(planes[0], x, y);
                const uint8_t c1 = broadcast ? c0 : sample_plane(planes[1], x, y);
                const uint8_t c2 = broadcast ? c0 : sample_plane(planes[2], x, y);
                const uint8_t c3 = has_fourth ? sample_plane(planes[3], x, y) : 255;

                to_rgb(transform, c0, c1, c2, c3, &out[x * 4 + 0], &out[x * 4 + 1], &out[x * 4 + 2]);
                out[x * 4 + 3] = has_alpha ? c3 : 255;
            }
        }
    }
}

static int reduced_extent(OPJ_UINT32 lo, OPJ_UINT32 hi, int reduce) {
    if (hi <= lo)
        return 0;

    const uint64_t span = (uint64_t) hi - (uint64_t) lo;
    const uint64_t scale = (uint64_t) 1 << reduce;
    const uint64_t extent = (span + scale - 1) / scale;

    return extent > (uint64_t) INT_MAX ? 0 : (int) extent;
}

static ColorTransform color_transform_for(const opj_image_t &image) {
    switch (image.color_space) {
        case OPJ_CLRSPC_SYCC:
        case OPJ_CLRSPC_EYCC:
            return image.numcomps >= 3 ? ColorTransform::Ycc : ColorTransform::None;

        case OPJ_CLRSPC_CMYK:
            return image.numcomps >= 4 ? ColorTransform::Cmyk : ColorTransform::None;

        default:
            return ColorTransform::None;
    }
}

extern "C" {

J2K_API const char *get_last_j2k_error(void) { return last_j2k_error; }

J2K_API bool decode_j2k_rgba8_from_memory(const uint8_t *data, size_t size, int reduce, uint8_t **out_pixels,
                                          int *width, int *height, int *stride_bytes, uint8_t **out_icc,
                                          int *out_icc_size) {

    if (!data || size == 0 || !out_pixels || !width || !height || !stride_bytes) {
        set_error("Invalid arguments.");
        return false;
    }

    *out_pixels = nullptr;
    *width = *height = *stride_bytes = 0;
    if (out_icc)
        *out_icc = nullptr;

    if (out_icc_size)
        *out_icc_size = 0;

    clear_error();

    const bool jp2 = is_jp2(data, size);
    const bool j2k = is_j2k_codestream(data, size);
    if (!jp2 && !j2k) {
        set_error("Input is not JP2 or J2K codestream (signature mismatch).");
        return false;
    }

    reduce = clamp_int(reduce, 0, 31);

    OpjSession session;

    session.codec = jp2 ? opj_create_decompress(OPJ_CODEC_JP2) : opj_create_decompress(OPJ_CODEC_J2K);
    if (!session.codec) {
        set_error("Failed to create OpenJPEG decompressor.");
        return false;
    }

    opj_set_error_handler(session.codec, opj_error_callback, nullptr);
    opj_set_warning_handler(session.codec, opj_warning_callback, nullptr);
    opj_set_info_handler(session.codec, opj_info_callback, nullptr);

    opj_dparameters_t params;
    opj_set_default_decoder_parameters(&params);
    params.cp_reduce = (OPJ_UINT32) reduce;

    if (!opj_setup_decoder(session.codec, &params)) {
        set_fallback_error("opj_setup_decoder failed.");
        return false;
    }

    MemStream ms;
    ms.data = data;
    ms.size = size;
    ms.pos = 0;

    session.stream = create_mem_stream(&ms);
    if (!session.stream) {
        set_error("Failed to create OpenJPEG memory stream.");
        return false;
    }

    if (!opj_read_header(session.stream, session.codec, &session.image) || !session.image) {
        set_fallback_error("opj_read_header failed.");
        return false;
    }

    if (!opj_decode(session.codec, session.stream, session.image)) {
        set_fallback_error("opj_decode failed.");
        return false;
    }

    const opj_image_t &image = *session.image;

    const int w = reduced_extent(image.x0, image.x1, reduce);
    const int h = reduced_extent(image.y0, image.y1, reduce);
    if (w <= 0 || h <= 0) {
        set_error("Decoded image has invalid dimensions.");
        return false;
    }

    const int comps = (int) image.numcomps;
    if (comps < 1) {
        set_error("Decoded image has no components.");
        return false;
    }

    if (w > INT_MAX / 4 || (uint64_t) w * (uint64_t) h > (uint64_t) (SIZE_MAX / 4)) {
        set_error("Decoded image is too large: %dx%d.", w, h);
        return false;
    }

    const bool is_gray = (comps == 1 || comps == 2);
    const ColorTransform transform = is_gray ? ColorTransform::None : color_transform_for(image);
    const bool fourth_is_black = transform == ColorTransform::Cmyk;

    Plane planes[4];
    planes[0].comp = &image.comps[0];
    planes[1].comp = is_gray ? &image.comps[0] : &image.comps[1];
    planes[2].comp = is_gray ? &image.comps[0] : &image.comps[2];

    if (comps == 2)
        planes[3].comp = &image.comps[1];

    else if (comps >= 4)
        planes[3].comp = &image.comps[3];

    const bool has_alpha = planes[3].comp != nullptr && !fourth_is_black;

    bool fast_path = true;
    for (auto &plane: planes) {
        if (!plane.comp)
            continue;

        if (!plane.comp->data) {
            set_error("Decoded image is missing component data.");
            return false;
        }

        plane.scale = make_scale_params(*plane.comp);
        fast_path = fast_path && is_full_res(*plane.comp, w, h);
    }

    const int stride = w * 4;
    uint8_t *buffer = (uint8_t *) std::malloc((size_t) stride * (size_t) h);
    if (!buffer) {
        set_error("Failed to allocate RGBA8 output buffer (%dx%d).", w, h);
        return false;
    }

    interleave_rgba(planes, transform, has_alpha, fast_path, buffer, stride, w, h);

    {
        std::lock_guard<std::mutex> lock(j2k_alloc_mutex);
        j2k_allocated_ptrs.insert(buffer);
    }

    // Best effort: a file with no profile, and a profile too big to copy, both leave the caller
    // with null - an un-tagged image, which is exactly how it is treated downstream.
    if (out_icc && out_icc_size && image.icc_profile_buf && image.icc_profile_len > 0) {
        uint8_t *icc_copy = (uint8_t *) std::malloc(image.icc_profile_len);
        if (icc_copy) {
            std::memcpy(icc_copy, image.icc_profile_buf, image.icc_profile_len);
            {
                std::lock_guard<std::mutex> lock(j2k_alloc_mutex);
                j2k_allocated_ptrs.insert(icc_copy);
            }
            *out_icc = icc_copy;
            *out_icc_size = (int) image.icc_profile_len;
        }
    }

    clear_error();

    *out_pixels = buffer;
    *width = w;
    *height = h;
    *stride_bytes = stride;
    return true;
}

J2K_API void free_j2k_pixels(uint8_t *ptr) {
    if (ptr == nullptr)
        return;

    std::lock_guard<std::mutex> lock(j2k_alloc_mutex);
    auto it = j2k_allocated_ptrs.find(ptr);
    if (it == j2k_allocated_ptrs.end())
        return;

    j2k_allocated_ptrs.erase(it);
    std::free(ptr);
}

} // extern "C"
