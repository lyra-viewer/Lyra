// -----------------------------------------------------------------------------
// exr_native — C ABI over OpenEXR, consumed from managed code (Lyra.Imaging's
// ExrNative). This header is the contract: the .cpp includes it so a signature
// change here is a compile error there, and the managed P/Invoke declarations
// mirror it by hand.
//
// Conventions shared by every Lyra native wrapper:
//   * Entry points are extern "C" and cdecl, and never let an exception escape.
//   * A `bool` return is the 1-byte C++/C bool both targeted ABIs use, which the
//     managed side marshals as UnmanagedType.I1.
//   * On failure the call returns false, leaves every out-parameter zeroed, and
//     leaves a reason in get_last_exr_error().
//   * Buffers handed back are owned by the caller and released with the matching
//     free_* function - never with the platform free().
// -----------------------------------------------------------------------------

#pragma once

#include <stdint.h>

#ifndef __cplusplus
#include <stdbool.h>
#endif

// Always an export: nothing links against this library at build time - the
// managed side loads it and resolves symbols by name - so there is no import case.
#ifdef _WIN32
#define EXR_API __declspec(dllexport)
#else
#define EXR_API __attribute__((visibility("default")))
#endif

#ifdef __cplusplus
extern "C" {
#endif

typedef struct {
    int32_t bits_per_channel;
    int32_t is_float;
    int32_t has_alpha;
    int32_t is_gray;
    int32_t custom_primaries; // header overrides the default Rec.709 chromaticities
} exr_info;

EXR_API const char *get_last_exr_error(void);

EXR_API bool load_exr_rgba(const char *path, float **out_pixels, int *width, int *height, exr_info *out_info);

EXR_API bool load_exr_rgba_mem(const void *data, uint64_t size, float **out_pixels, int *width, int *height,
                               exr_info *out_info);

EXR_API void free_exr_pixels(float *ptr);

#ifdef __cplusplus
} // extern "C"
#endif
