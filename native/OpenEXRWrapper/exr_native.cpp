#include <OpenEXR/ImfArray.h>
#include <OpenEXR/ImfRgba.h>
#include <OpenEXR/ImfRgbaFile.h>
#include <OpenEXR/ImfThreading.h>
#include <cstdio>
#include <mutex>
#include <unordered_set>

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

extern "C" {

EXR_API const char *get_last_exr_error() { return last_exr_error; }

EXR_API bool load_exr_rgba(const char *path, float **out_pixels, int *width, int *height) {
    static std::once_flag exr_init_flag;
    std::call_once(exr_init_flag, []() {
        unsigned n = std::thread::hardware_concurrency();
        Imf::setGlobalThreadCount(n ? static_cast<int>(n) : 1);
        printf("[EXR] Using %u threads for OpenEXR\n", n);
    });

    try {
        Imf::RgbaInputFile file(path);
        Imath::Box2i dw = file.dataWindow();
        int w = dw.max.x - dw.min.x + 1;
        int h = dw.max.y - dw.min.y + 1;

        *width = w;
        *height = h;

        Imf::Array2D<Imf::Rgba> pixels;
        pixels.resizeErase(h, w);
        file.setFrameBuffer(&pixels[0][0] - dw.min.x - dw.min.y * w, 1, w);
        file.readPixels(dw.min.y, dw.max.y);

        *out_pixels = (float *) malloc(sizeof(float) * w * h * 4);
        if (!*out_pixels) {
            snprintf(last_exr_error, sizeof(last_exr_error), "Failed to allocate memory for EXR output buffer.");
            return false;
        }

        {
            std::lock_guard<std::mutex> lock(exr_alloc_mutex);
            exr_allocated_ptrs.insert(*out_pixels);
        }

        for (int y = 0; y < h; ++y) {
            for (int x = 0; x < w; ++x) {
                int i = y * w + x;
                (*out_pixels)[i * 4 + 0] = pixels[y][x].r;
                (*out_pixels)[i * 4 + 1] = pixels[y][x].g;
                (*out_pixels)[i * 4 + 2] = pixels[y][x].b;
                (*out_pixels)[i * 4 + 3] = pixels[y][x].a;
            }
        }

        last_exr_error[0] = '\0';
        return true;
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
