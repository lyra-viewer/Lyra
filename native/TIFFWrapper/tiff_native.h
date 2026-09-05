// -----------------------------------------------------------------------------
// tiff_native — C ABI over libtiff, consumed from managed code (Lyra.Imaging's
// TiffNative). This header is the contract: the .cpp includes it so a signature
// change here is a compile error there, and the managed P/Invoke declarations
// mirror it by hand.
//
// Conventions shared by every Lyra native wrapper:
//   * Entry points are extern "C" and cdecl, and never let an exception escape.
//   * A `bool` return is the 1-byte C++/C bool both targeted ABIs use, which the
//     managed side marshals as UnmanagedType.I1.
//   * On failure the call returns false, leaves every out-parameter zeroed, and
//     leaves a reason in get_last_tiff_error().
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
#define TIFF_API __declspec(dllexport)
#else
#define TIFF_API __attribute__((visibility("default")))
#endif

#ifdef __cplusplus
extern "C" {
#endif

TIFF_API const char *get_last_tiff_error(void);

TIFF_API bool load_tiff_rgba(const char *path, uint8_t **out_pixels, int *width, int *height, uint8_t **out_icc,
                             int *out_icc_size);

TIFF_API bool load_tiff_rgba_mem(const uint8_t *data, uint64_t size, uint8_t **out_pixels, int *width, int *height,
                                 uint8_t **out_icc, int *out_icc_size);

TIFF_API void free_tiff_pixels(uint8_t *ptr);

#ifdef __cplusplus
} // extern "C"
#endif
