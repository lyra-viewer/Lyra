#include <OpenEXR/ImfArray.h>
#include <OpenEXR/ImfChannelList.h>
#include <OpenEXR/ImfChromaticities.h>
#include <OpenEXR/ImfFrameBuffer.h>
#include <OpenEXR/ImfHeader.h>
#include <OpenEXR/ImfInputFile.h>
#include <OpenEXR/ImfRgba.h>
#include <OpenEXR/ImfRgbaFile.h>
#include <OpenEXR/ImfStandardAttributes.h>
#include <OpenEXR/ImfThreading.h>
#include <cmath>
#include <cstddef>
#include <cstdint>
#include <cstdio>
#include <cstdlib>
#include <cstring>
#include <stdexcept>
#include <string>
#include <thread>
#include <mutex>
#include <unordered_set>

typedef struct {
    int32_t bits_per_channel;
    int32_t is_float;
    int32_t has_alpha;
    int32_t is_gray;
    int32_t custom_primaries; // header overrides the default Rec.709 chromaticities
} exr_info;

#ifdef _WIN32
#define EXR_API __declspec(dllexport)
#else
#define EXR_API __attribute__((visibility("default")))
#endif

#ifdef __clang__
#define THREAD_LOCAL __thread
#else
#define THREAD_LOCAL thread_local
#endif

static THREAD_LOCAL char last_exr_error[512] = "";
static std::unordered_set<void *> exr_allocated_ptrs;
static std::mutex exr_alloc_mutex;

// Allocates the interleaved RGBA float buffer and registers it with the double-free
// guard that free_exr_pixels checks. Returns nullptr (error message set) on OOM.
static float *alloc_rgba(int w, int h) {
    auto *buffer = (float *) malloc(sizeof(float) * (size_t) w * (size_t) h * 4);
    if (!buffer) {
        snprintf(last_exr_error, sizeof(last_exr_error), "Failed to allocate memory for EXR output buffer.");
        return nullptr;
    }

    std::lock_guard<std::mutex> lock(exr_alloc_mutex);
    exr_allocated_ptrs.insert(buffer);
    return buffer;
}

static void discard_rgba(float *buffer) {
    {
        std::lock_guard<std::mutex> lock(exr_alloc_mutex);
        exr_allocated_ptrs.erase(buffer);
    }
    free(buffer);
}

static Imf::Slice sampled_slice(float *buffer, int component, const Imath::Box2i &dw,
                                size_t x_stride, size_t y_stride, const Imf::Channel &channel,
                                double fill) {
    
    char *base = (char *) (buffer + component)
                 - (ptrdiff_t) (dw.min.x / channel.xSampling) * (ptrdiff_t) x_stride
                 - (ptrdiff_t) (dw.min.y / channel.ySampling) * (ptrdiff_t) y_stride;

    return Imf::Slice(Imf::FLOAT, base, x_stride, y_stride, channel.xSampling, channel.ySampling, fill);
}

static void expand_subsampled(float *rgba, int w, int h, int component, int x_sampling, int y_sampling) {
    if (x_sampling <= 1 && y_sampling <= 1)
        return;

    for (int y = h - 1; y >= 0; --y) {
        const float *src_row = rgba + (size_t) (y / y_sampling) * (size_t) w * 4;
        float *dst_row = rgba + (size_t) y * (size_t) w * 4;

        for (int x = w - 1; x >= 0; --x)
            dst_row[x * 4 + component] = src_row[(x / x_sampling) * 4 + component];
    }
}

// Name of the lone non-alpha channel when this is a single-channel EXR that
// RgbaInputFile would misread - e.g. a displacement/height/mask map written with just
// "R" (ProEXR does this), or a bare "Z"/"depth" plate. RgbaInputFile fills the channels
// the file lacks with 0, so such an image renders as a red-only (or black) plate instead
// of the grayscale it is. Returns "" when the normal RGB path applies.
static std::string single_channel_name(const Imf::Header &header) {
    const Imf::ChannelList &channels = header.channels();

    // Luminance/chroma files carry "Y". RgbaInputFile already converts those to RGB
    // (broadcasting Y when there is no chroma, and upsampling RY/BY when there is),
    // so leave them to it.
    if (channels.findChannel("Y") != nullptr)
        return std::string();

    std::string name;
    int count = 0;

    for (auto it = channels.begin(); it != channels.end(); ++it) {
        if (strcmp(it.name(), "A") == 0)
            continue;
        
        if (++count > 1)
            return std::string();

        name = it.name();
    }

    return count == 1 ? name : std::string();
}

// Chromaticity coordinates compared with a tolerance rather than exactly: writers
// routinely round-trip Rec.709 through text or float32 and store it as 0.640009/0.330022,
// which an exact compare would report as a custom color space. 1e-4 is orders of magnitude
// below the gap to any other real primary set (Rec.709 red x=0.640 vs DCI-P3 0.680 vs
// Rec.2020 0.708).
static bool same_point(const Imath::V2f &a, const Imath::V2f &b) {
    const float tolerance = 1e-4f;
    return fabsf(a.x - b.x) <= tolerance && fabsf(a.y - b.y) <= tolerance;
}

static bool same_primaries(const Imf::Chromaticities &a, const Imf::Chromaticities &b) {
    return same_point(a.red, b.red) && same_point(a.green, b.green) && same_point(a.blue, b.blue) && same_point(a.white, b.white);
}

static void describe_header(const Imf::Header &header, exr_info *info) {
    if (info == nullptr)
        return;

    memset(info, 0, sizeof(*info));

    const Imf::ChannelList &channels = header.channels();
    int color_channels = 0;

    for (auto it = channels.begin(); it != channels.end(); ++it) {
        const bool is_alpha = strcmp(it.name(), "A") == 0;
        if (is_alpha) {
            info->has_alpha = 1;
            continue;
        }

        ++color_channels;

        // Sample format is taken from the color channels only: a half-float image with a
        // full-float alpha is a 16-bit image, not a 32-bit one.
        const Imf::PixelType type = it.channel().type;
        const int32_t bits = type == Imf::HALF ? 16 : 32;
        if (bits > info->bits_per_channel)
            info->bits_per_channel = bits;
        if (type != Imf::UINT)
            info->is_float = 1;
    }

    // Alpha-only files are degenerate, but reporting no depth at all would be worse.
    if (color_channels == 0) {
        const Imf::Channel *alpha = channels.findChannel("A");
        if (alpha != nullptr) {
            info->bits_per_channel = alpha->type == Imf::HALF ? 16 : 32;
            info->is_float = alpha->type != Imf::UINT ? 1 : 0;
        }
    }

    // Gray when there is nothing to make color out of - one lone channel, whether that is
    // "R", "Z" or a "Y" luminance plate with no RY/BY chroma beside it.
    info->is_gray = color_channels == 1 ? 1 : 0;

    // EXR is always scene-linear, so the only thing that varies in the working space is
    // the primaries - Rec.709 unless the file carries a chromaticities attribute saying
    // otherwise.
    if (Imf::hasChromaticities(header))
        info->custom_primaries = same_primaries(Imf::chromaticities(header), Imf::Chromaticities()) ? 0 : 1;
}

// Reads a single-channel EXR, broadcasting that channel into R, G and B so it comes out
// gray. Alpha comes from an "A" channel when the file has one, and is opaque otherwise -
// matching what RgbaInputFile does for RGB files without alpha.
static bool load_single_channel(const char *path, const std::string &channel,
                                float **out_pixels, int *width, int *height) {
    Imf::InputFile file(path);
    const Imath::Box2i dw = file.header().dataWindow();
    const int w = dw.max.x - dw.min.x + 1;
    const int h = dw.max.y - dw.min.y + 1;

    float *buffer = alloc_rgba(w, h);
    if (!buffer)
        return false;

    try {
        const size_t x_stride = sizeof(float) * 4;
        const size_t y_stride = x_stride * (size_t) w;

        const Imf::ChannelList &channels = file.header().channels();
        const Imf::Channel *gray = channels.findChannel(channel.c_str());
        const Imf::Channel *alpha = channels.findChannel("A");

        if (gray == nullptr)
            throw std::runtime_error("single channel vanished between header read and decode");

        Imf::FrameBuffer fb;
        fb.insert(channel.c_str(), sampled_slice(buffer, 0, dw, x_stride, y_stride, *gray, 0.0));
        if (alpha != nullptr)
            fb.insert("A", sampled_slice(buffer, 3, dw, x_stride, y_stride, *alpha, 1.0));

        file.setFrameBuffer(fb);
        file.readPixels(dw.min.y, dw.max.y);

        // Before the broadcast, so a subsampled channel is whole first.
        expand_subsampled(buffer, w, h, 0, gray->xSampling, gray->ySampling);
        if (alpha != nullptr)
            expand_subsampled(buffer, w, h, 3, alpha->xSampling, alpha->ySampling);

        const size_t count = (size_t) w * (size_t) h;
        for (size_t i = 0; i < count; ++i) {
            const float value = buffer[i * 4 + 0];
            buffer[i * 4 + 1] = value;
            buffer[i * 4 + 2] = value;
            if (alpha == nullptr)
                buffer[i * 4 + 3] = 1.0f;
        }
    } catch (...) {
        discard_rgba(buffer);
        throw;
    }

    *out_pixels = buffer;
    *width = w;
    *height = h;
    return true;
}

extern "C" {

EXR_API const char *get_last_exr_error() { return last_exr_error; }

EXR_API bool load_exr_rgba(const char *path, float **out_pixels, int *width, int *height, exr_info *out_info) {
    static std::once_flag exr_init_flag;
    std::call_once(exr_init_flag, []() {
        unsigned n = std::thread::hardware_concurrency();
        Imf::setGlobalThreadCount(n ? static_cast<int>(n) : 1);
        printf("[EXR] Using %u threads for OpenEXR\n", n);
    });

    if (out_info != nullptr)
        memset(out_info, 0, sizeof(*out_info));

    try {
        std::string gray_channel;

        {
            Imf::RgbaInputFile file(path);
            describe_header(file.header(), out_info);
            gray_channel = single_channel_name(file.header());

            if (gray_channel.empty()) {
                Imath::Box2i dw = file.dataWindow();
                int w = dw.max.x - dw.min.x + 1;
                int h = dw.max.y - dw.min.y + 1;

                Imf::Array2D<Imf::Rgba> pixels;
                pixels.resizeErase(h, w);
                file.setFrameBuffer(&pixels[0][0] - dw.min.x - dw.min.y * w, 1, w);
                file.readPixels(dw.min.y, dw.max.y);

                float *buffer = alloc_rgba(w, h);
                if (!buffer) {
                    *out_pixels = nullptr;
                    *width = *height = 0;
                    return false;
                }

                for (int y = 0; y < h; ++y) {
                    for (int x = 0; x < w; ++x) {
                        int i = y * w + x;
                        buffer[i * 4 + 0] = pixels[y][x].r;
                        buffer[i * 4 + 1] = pixels[y][x].g;
                        buffer[i * 4 + 2] = pixels[y][x].b;
                        buffer[i * 4 + 3] = pixels[y][x].a;
                    }
                }

                *out_pixels = buffer;
                *width = w;
                *height = h;
                last_exr_error[0] = '\0';
                return true;
            }
        }

        if (load_single_channel(path, gray_channel, out_pixels, width, height)) {
            last_exr_error[0] = '\0';
            return true;
        }
    } catch (const std::exception &ex) {
        snprintf(last_exr_error, sizeof(last_exr_error), "EXR exception: %s", ex.what());
    } catch (...) {
        snprintf(last_exr_error, sizeof(last_exr_error), "Unknown EXR exception.");
    }
    *out_pixels = nullptr;
    *width = *height = 0;
    return false;
}

EXR_API void free_exr_pixels(float *ptr) {
    if (ptr == nullptr) {
        printf("[C++] Warning: free_exr_pixels called with null pointer!\n");
        fflush(stdout);
        return;
    }

    {
        std::lock_guard<std::mutex> lock(exr_alloc_mutex);
        if (exr_allocated_ptrs.find(ptr) == exr_allocated_ptrs.end()) {
            printf("[C++] Warning: Attempting to free unknown or already freed pointer %p!\n", ptr);
            fflush(stdout);
        } else {
            exr_allocated_ptrs.erase(ptr);
            free(ptr);
        }
    }
}
}
