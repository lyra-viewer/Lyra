// -----------------------------------------------------------------------------
// j2k_native — C ABI over OpenJPEG, consumed from managed code (Lyra.Imaging's
// J2KNative). This header is the contract: the .cpp includes it so a signature
// change here is a compile error there, and the managed P/Invoke declarations
// mirror it by hand.
//
// Conventions shared by every Lyra native wrapper:
//   * Entry points are extern "C" and cdecl, and never let an exception escape.
//   * A `bool` return is the 1-byte C++/C bool both targeted ABIs use, which the
//     managed side marshals as UnmanagedType.I1.
//   * On failure the call returns false, leaves every out-parameter zeroed, and
//     leaves a reason in get_last_j2k_error().
//   * Buffers handed back are owned by the caller and released with the matching
//     free_* function - never with the platform free().
// -----------------------------------------------------------------------------

#pragma once

#include <stddef.h>
#include <stdint.h>

#ifndef __cplusplus
#include <stdbool.h>
#endif

// Always an export: nothing links against this library at build time - the
// managed side loads it and resolves symbols by name - so there is no import case.
#ifdef _WIN32
#define J2K_API __declspec(dllexport)
#else
#define J2K_API __attribute__((visibility("default")))
#endif

#ifdef __cplusplus
extern "C" {
#endif

J2K_API const char *get_last_j2k_error(void);

J2K_API bool decode_j2k_rgba8_from_memory(const uint8_t *data, size_t size, int reduce, uint8_t **out_pixels,
                                          int *width, int *height, int *stride_bytes, uint8_t **out_icc,
                                          int *out_icc_size);

J2K_API void free_j2k_pixels(uint8_t *ptr);

#ifdef __cplusplus
} // extern "C"
#endif
