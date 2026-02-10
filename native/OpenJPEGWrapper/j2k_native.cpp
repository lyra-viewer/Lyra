#include <cstdio>
#include <cstdlib>
#include <cstring>
#include <mutex>
#include <openjpeg.h>
#include <unordered_set>

#ifdef _WIN32
#define J2K_API __declspec(dllexport)
#else
#define J2K_API __attribute__((visibility("default")))
#endif

#ifdef __clang__
#define THREAD_LOCAL __thread
#else
#define THREAD_LOCAL thread_local
#endif

static THREAD_LOCAL char last_j2k_error[512] = "";

static std::unordered_set<void *> j2k_allocated_ptrs;
static std::mutex j2k_alloc_mutex;

static void set_error(const char *msg) {
    if (!msg)
        msg = "Unknown error.";
    std::snprintf(last_j2k_error, sizeof(last_j2k_error), "%s", msg);
}

static void clear_error() { last_j2k_error[0] = '\0'; }

// OpenJPEG log callbacks
static void opj_error_callback(const char *msg, void * /*client_data*/) {
    if (!msg)
        return;
    std::snprintf(last_j2k_error, sizeof(last_j2k_error), "OpenJPEG: %s", msg);
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

    const size_t remaining = ms->size - ms->pos;
    size_t to_read = (size_t) p_nb_bytes;
    if (to_read > remaining)
        to_read = remaining;

    if (to_read > 0) {
        std::memcpy(p_buffer, ms->data + ms->pos, to_read);
        ms->pos += to_read;
        return (OPJ_SIZE_T) to_read;
    }

    return 0; // EOF
}

static OPJ_OFF_T mem_skip(OPJ_OFF_T p_nb_bytes, void *p_user_data) {
    auto *ms = reinterpret_cast<MemStream *>(p_user_data);
    if (!ms || p_nb_bytes <= 0)
        return (OPJ_OFF_T) -1;

    const size_t remaining = ms->size - ms->pos;
    if ((size_t) p_nb_bytes > remaining)
        return (OPJ_OFF_T) -1;

    ms->pos += (size_t) p_nb_bytes;
    return p_nb_bytes;
}

static OPJ_BOOL mem_seek(OPJ_OFF_T p_nb_bytes, void *p_user_data) {
    auto *ms = reinterpret_cast<MemStream *>(p_user_data);
    if (!ms || p_nb_bytes < 0)
        return OPJ_FALSE;

    const size_t new_pos = (size_t) p_nb_bytes;
    if (new_pos > ms->size)
        return OPJ_FALSE;

    ms->pos = new_pos;
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

extern "C" {

J2K_API const char *get_last_j2k_error() { return last_j2k_error; }

// Decode to RGBA8. reduce: 0=full, 1=half, 2=quarter...
J2K_API bool decode_j2k_rgba8_from_memory(const uint8_t *data, size_t size, int reduce, uint8_t **out_pixels,
                                          int *width, int *height, int *stride_bytes) {
    if (!data || size == 0 || !out_pixels || !width || !height || !stride_bytes) {
        set_error("Invalid arguments.");
        return false;
    }

    *out_pixels = nullptr;
    *width = *height = *stride_bytes = 0;
    clear_error();

    const bool jp2 = is_jp2(data, size);
    const bool j2k = is_j2k_codestream(data, size);
    if (!jp2 && !j2k) {
        set_error("Input is not JP2 or J2K codestream (signature mismatch).");
        return false;
    }

    opj_codec_t *codec = jp2 ? opj_create_decompress(OPJ_CODEC_JP2) : opj_create_decompress(OPJ_CODEC_J2K);

    if (!codec) {
        set_error("Failed to create OpenJPEG decompressor.");
        return false;
    }

    opj_set_error_handler(codec, opj_error_callback, nullptr);
    opj_set_warning_handler(codec, opj_warning_callback, nullptr);
    opj_set_info_handler(codec, opj_info_callback, nullptr);

    opj_dparameters_t params;
    opj_set_default_decoder_parameters(&params);
    if (reduce < 0)
        reduce = 0;
    params.cp_reduce = reduce;

    if (!opj_setup_decoder(codec, &params)) {
        opj_destroy_codec(codec);
        set_error("opj_setup_decoder failed.");
        return false;
    }

    MemStream ms;
    ms.data = data;
    ms.size = size;
    ms.pos = 0;

    opj_stream_t *stream = create_mem_stream(&ms);
    if (!stream) {
        opj_destroy_codec(codec);
        set_error("Failed to create OpenJPEG memory stream.");
        return false;
    }

    opj_image_t *image = nullptr;

    if (!opj_read_header(stream, codec, &image) || !image) {
        opj_stream_destroy(stream);
        opj_destroy_codec(codec);
        set_error("opj_read_header failed.");
        return false;
    }

    if (!opj_decode(codec, stream, image)) {
        opj_image_destroy(image);
        opj_stream_destroy(stream);
        opj_destroy_codec(codec);
        set_error("opj_decode failed.");
        return false;
    }

    const int w = (int) (image->x1 - image->x0);
    const int h = (int) (image->y1 - image->y0);
    if (w <= 0 || h <= 0) {
        opj_image_destroy(image);
        opj_stream_destroy(stream);
        opj_destroy_codec(codec);
        set_error("Decoded image has invalid dimensions.");
        return false;
    }

    const int comps = image->numcomps;
    if (comps < 1) {
        opj_image_destroy(image);
        opj_stream_destroy(stream);
        opj_destroy_codec(codec);
        set_error("Decoded image has no components.");
        return false;
    }

    const int stride = w * 4;
    uint8_t *buffer = (uint8_t *) std::malloc((size_t) stride * (size_t) h);
    if (!buffer) {
        opj_image_destroy(image);
        opj_stream_destroy(stream);
        opj_destroy_codec(codec);
        set_error("Failed to allocate RGBA8 output buffer.");
        return false;
    }

    {
        std::lock_guard<std::mutex> lock(j2k_alloc_mutex);
        j2k_allocated_ptrs.insert(buffer);
    }

    const opj_image_comp_t *cap = nullptr;
    // Pragmatic alpha handling:
    // - Gray+Alpha => comp[1]
    // - RGBA-ish   => comp[3] (if present)
    if (comps == 2)
        cap = &image->comps[1];
    else if (comps >= 4)
        cap = &image->comps[3];

    auto full_res = [&](const opj_image_comp_t &c) -> bool {
        return c.dx == 1 && c.dy == 1 && c.w == (OPJ_UINT32) w && c.h == (OPJ_UINT32) h;
    };

    const bool is_gray = (comps == 1 || comps == 2);
    bool fast_path = false;

    if (is_gray) {
        fast_path = full_res(image->comps[0]) && (cap ? full_res(*cap) : true);
    } else {
        // RGB(A)
        fast_path = comps >= 3 && full_res(image->comps[0]) && full_res(image->comps[1]) && full_res(image->comps[2]) &&
                    (cap ? full_res(*cap) : true);
    }

    // SLOW PATH helper: sample component value at full-res (x,y) respecting dx/dy.
    auto sample_comp_u8 = [&](const opj_image_comp_t &comp, const ScaleParams &sp, int x, int y) -> uint8_t {
        const int dx = (comp.dx > 0) ? (int) comp.dx : 1;
        const int dy = (comp.dy > 0) ? (int) comp.dy : 1;

        int cx = x / dx;
        int cy = y / dy;
        cx = clamp_int(cx, 0, (int) comp.w - 1);
        cy = clamp_int(cy, 0, (int) comp.h - 1);

        const int idx = cy * (int) comp.w + cx;
        return scale_to_u8(comp.data[idx], sp);
    };

    if (fast_path) {
        // FAST PATH: no division, direct row pointers.
        if (is_gray) {
            const auto &cg = image->comps[0];
            const ScaleParams spg = make_scale_params(cg);

            const int *a_data = nullptr;
            ScaleParams spa{};
            if (cap) {
                spa = make_scale_params(*cap);
                a_data = cap->data;
            }

            for (int y = 0; y < h; ++y) {
                const int *g_row = cg.data + y * (int) cg.w;
                const int *a_row = a_data ? (a_data + y * (int) cap->w) : nullptr;
                uint8_t *out = buffer + (size_t) y * (size_t) stride;

                for (int x = 0; x < w; ++x) {
                    const uint8_t g = scale_to_u8(g_row[x], spg);
                    const uint8_t a = a_row ? scale_to_u8(a_row[x], spa) : 255;

                    out[x * 4 + 0] = g;
                    out[x * 4 + 1] = g;
                    out[x * 4 + 2] = g;
                    out[x * 4 + 3] = a;
                }
            }
        } else {
            const auto &cr = image->comps[0];
            const auto &cg = image->comps[1];
            const auto &cb = image->comps[2];

            const ScaleParams spr = make_scale_params(cr);
            const ScaleParams spg = make_scale_params(cg);
            const ScaleParams spb = make_scale_params(cb);

            const int *a_data = nullptr;
            ScaleParams spa{};
            if (cap) {
                spa = make_scale_params(*cap);
                a_data = cap->data;
            }

            for (int y = 0; y < h; ++y) {
                const int *r_row = cr.data + y * (int) cr.w;
                const int *g_row = cg.data + y * (int) cg.w;
                const int *b_row = cb.data + y * (int) cb.w;
                const int *a_row = a_data ? (a_data + y * (int) cap->w) : nullptr;

                uint8_t *out = buffer + (size_t) y * (size_t) stride;

                for (int x = 0; x < w; ++x) {
                    const uint8_t r = scale_to_u8(r_row[x], spr);
                    const uint8_t g = scale_to_u8(g_row[x], spg);
                    const uint8_t b = scale_to_u8(b_row[x], spb);
                    const uint8_t a = a_row ? scale_to_u8(a_row[x], spa) : 255;

                    out[x * 4 + 0] = r;
                    out[x * 4 + 1] = g;
                    out[x * 4 + 2] = b;
                    out[x * 4 + 3] = a;
                }
            }
        }
    } else {
        // SLOW PATH: handles subsampling properly.
        if (is_gray) {
            const auto &cg = image->comps[0];
            const ScaleParams spg = make_scale_params(cg);
            ScaleParams spa{};
            if (cap)
                spa = make_scale_params(*cap);

            for (int y = 0; y < h; ++y) {
                uint8_t *out = buffer + (size_t) y * (size_t) stride;
                for (int x = 0; x < w; ++x) {
                    const uint8_t g = sample_comp_u8(cg, spg, x, y);
                    const uint8_t a = (cap != nullptr) ? sample_comp_u8(*cap, spa, x, y) : 255;

                    out[x * 4 + 0] = g;
                    out[x * 4 + 1] = g;
                    out[x * 4 + 2] = g;
                    out[x * 4 + 3] = a;
                }
            }
        } else {
            const auto &cr = image->comps[0];
            const auto &cg = image->comps[1];
            const auto &cb = image->comps[2];

            const ScaleParams spr = make_scale_params(cr);
            const ScaleParams spg = make_scale_params(cg);
            const ScaleParams spb = make_scale_params(cb);
            ScaleParams spa{};
            if (cap)
                spa = make_scale_params(*cap);

            for (int y = 0; y < h; ++y) {
                uint8_t *out = buffer + (size_t) y * (size_t) stride;
                for (int x = 0; x < w; ++x) {
                    const uint8_t r = sample_comp_u8(cr, spr, x, y);
                    const uint8_t g = sample_comp_u8(cg, spg, x, y);
                    const uint8_t b = sample_comp_u8(cb, spb, x, y);
                    const uint8_t a = (cap != nullptr) ? sample_comp_u8(*cap, spa, x, y) : 255;

                    out[x * 4 + 0] = r;
                    out[x * 4 + 1] = g;
                    out[x * 4 + 2] = b;
                    out[x * 4 + 3] = a;
                }
            }
        }
    }

    opj_image_destroy(image);
    opj_stream_destroy(stream);
    opj_destroy_codec(codec);

    clear_error();
    *out_pixels = buffer;
    *width = w;
    *height = h;
    *stride_bytes = stride;
    return true;
}

J2K_API void free_j2k_pixels(uint8_t *ptr) {
    if (ptr == nullptr) {
        std::printf("[C++] Warning: free_j2k_pixels called with null pointer!\n");
        std::fflush(stdout);
        return;
    }

    std::lock_guard<std::mutex> lock(j2k_alloc_mutex);
    if (j2k_allocated_ptrs.find(ptr) == j2k_allocated_ptrs.end()) {
        std::printf("[C++] Warning: Attempting to free unknown or already freed pointer %p!\n", ptr);
        std::fflush(stdout);
        return;
    }

    j2k_allocated_ptrs.erase(ptr);
    std::free(ptr);
}

} // extern "C"
