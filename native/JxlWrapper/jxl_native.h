// -----------------------------------------------------------------------------
// jxl_native — C ABI over libjxl, consumed from managed code (Lyra.Imaging's
// JxlNative). This header is the contract: the .cpp includes it so a signature
// change here is a compile error there, and the managed P/Invoke declarations
// mirror it by hand.
//
// Conventions shared by every Lyra native wrapper:
//   * Entry points are extern "C" and cdecl, and never let an exception escape.
//   * A `bool` return is the 1-byte C++/C bool both targeted ABIs use, which the
//     managed side marshals as UnmanagedType.I1.
//   * On failure the call returns false, leaves every out-parameter zeroed, and
//     leaves a reason in get_last_jxl_error().
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
#define JXL_NATIVE_API __declspec(dllexport)
#else
#define JXL_NATIVE_API __attribute__((visibility("default")))
#endif

#ifdef __cplusplus
extern "C" {
#endif

JXL_NATIVE_API const char *get_last_jxl_error(void);

JXL_NATIVE_API bool decode_jxl_from_memory(const uint8_t *data, size_t size, int *out_width, int *out_height,
                                           int *out_is_hdr, int *out_bits_per_sample, int *out_has_alpha,
                                           int *out_has_animation, uint8_t **out_pixels);

JXL_NATIVE_API void free_jxl_pixels(void *ptr);

#ifdef __cplusplus
} // extern "C"
#endif
