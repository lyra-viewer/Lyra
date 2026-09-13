// -----------------------------------------------------------------------------
// basis_native — C ABI over the Basis Universal transcoder, consumed from managed
// code (Lyra.Imaging's BasisNative). This header is the contract: the .cpp includes
// it so a signature change here is a compile error there, and the managed P/Invoke
// declarations mirror it by hand.
//
// Conventions shared by every Lyra native wrapper:
//   * Entry points are extern "C" and cdecl, and never let an exception escape.
//   * A `bool` return is the 1-byte C++/C bool both targeted ABIs use, which the
//     managed side marshals as UnmanagedType.I1.
//   * On failure the call returns false, leaves every out-parameter zeroed, and
//     leaves a reason in get_last_basis_error().
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
#define BASIS_API __declspec(dllexport)
#else
#define BASIS_API __attribute__((visibility("default")))
#endif

#ifdef __cplusplus
extern "C" {
#endif

BASIS_API const char *get_last_basis_error(void);

BASIS_API bool basis_decode_ktx2_rgba(const uint8_t *data, int size, int level, int layer, int face,
                                      uint8_t **out_pixels, int *out_width, int *out_height);

BASIS_API void basis_free_pixels(uint8_t *ptr);

#ifdef __cplusplus
} // extern "C"
#endif
