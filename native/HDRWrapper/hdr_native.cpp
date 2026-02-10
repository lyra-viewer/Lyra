#include <cstdio>
#include <cstdlib>
#include <mutex>
#include <unordered_set>
#include "rgbe.h"

#ifdef _WIN32
#define HDR_API __declspec(dllexport)
#else
#define HDR_API __attribute__((visibility("default")))
#endif

#ifdef __clang__
#define THREAD_LOCAL __thread
#else
#define THREAD_LOCAL thread_local
#endif

static THREAD_LOCAL char last_hdr_error[512] = "";
static std::unordered_set<void *> hdr_allocated_ptrs;
static std::mutex hdr_alloc_mutex;

extern "C" {

HDR_API const char* get_last_hdr_error() {
    return last_hdr_error;
}

HDR_API bool load_hdr_rgba(const char *path, float **out_pixels, int *width, int *height) {
    FILE *file = fopen(path, "rb");
    if (!file) {
        snprintf(last_hdr_error, sizeof(last_hdr_error), "Failed to open HDR file.");
        return false;
    }

    if (RGBE_ReadHeader(file, width, height, nullptr) < 0) {
        fclose(file);
        snprintf(last_hdr_error, sizeof(last_hdr_error), "Failed to read HDR header.");
        return false;
    }

    int totalPixels = (*width) * (*height);
    float *buffer = (float *) malloc(sizeof(float) * totalPixels * 4);
    if (!buffer) {
        fclose(file);
        snprintf(last_hdr_error, sizeof(last_hdr_error), "Failed to allocate memory for HDR output buffer.");
        return false;
    }

    {
        std::lock_guard<std::mutex> lock(hdr_alloc_mutex);
        hdr_allocated_ptrs.insert(buffer);
    }

    float *rgb = (float *) malloc(sizeof(float) * totalPixels * 3);
    if (!rgb) {
        fclose(file);
        {
            std::lock_guard<std::mutex> lock(hdr_alloc_mutex);
            hdr_allocated_ptrs.erase(buffer);
        }
        free(buffer);
        snprintf(last_hdr_error, sizeof(last_hdr_error), "Failed to allocate memory for HDR intermediate RGB buffer.");
        return false;
    }

    if (RGBE_ReadPixels_RLE(file, rgb, *width, *height) < 0) {
        fclose(file);
        {
            std::lock_guard<std::mutex> lock(hdr_alloc_mutex);
            hdr_allocated_ptrs.erase(buffer);
        }
        free(buffer);
        free(rgb);
        snprintf(last_hdr_error, sizeof(last_hdr_error), "Failed to read HDR pixels (RLE).\n");
        return false;
    }

    fclose(file);

    for (int i = 0; i < totalPixels; ++i) {
        buffer[i * 4 + 0] = rgb[i * 3 + 0];
        buffer[i * 4 + 1] = rgb[i * 3 + 1];
        buffer[i * 4 + 2] = rgb[i * 3 + 2];
        buffer[i * 4 + 3] = 1.0f; // Alpha
    }

    free(rgb);
    last_hdr_error[0] = '\0';
    *out_pixels = buffer;
    return true;
}

HDR_API void free_hdr_pixels(float *ptr) {
    if (ptr == nullptr) {
        printf("[C++] Warning: free_hdr_pixels called with null pointer!\n");
        fflush(stdout);
        return;
    }

    {
        std::lock_guard<std::mutex> lock(hdr_alloc_mutex);
        if (hdr_allocated_ptrs.find(ptr) == hdr_allocated_ptrs.end()) {
            printf("[C++] Warning: Attempting to free unknown or already freed pointer %p!\n", ptr);
            fflush(stdout);
        } else {
            hdr_allocated_ptrs.erase(ptr);
            free(ptr);
        }
    }
}
}
