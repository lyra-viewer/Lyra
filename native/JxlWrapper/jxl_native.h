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

#ifdef _WIN32
#define JXL_NATIVE_API __declspec(dllexport)
#else
#define JXL_NATIVE_API __attribute__((visibility("default")))
#endif

#ifdef __cplusplus
extern "C" {
#endif

JXL_NATIVE_API const char *get_last_jxl_error(void);

// Pixels come straight-alpha RGBA (float for HDR, else 8-bit), already oriented:
// width and height are after the file's orientation.
JXL_NATIVE_API bool decode_jxl_from_memory(const uint8_t *data, size_t size, int *out_width, int *out_height,
                                           int *out_is_hdr, int *out_bits_per_sample, int *out_has_alpha,
                                           int *out_has_animation, uint8_t **out_pixels);

JXL_NATIVE_API void free_jxl_pixels(void *ptr);

typedef struct jxl_animation jxl_animation;

typedef struct {
    int32_t width;
    int32_t height;
    int32_t is_hdr;
    int32_t bits_per_sample;
    int32_t has_alpha;
    int32_t frame_count;
    int32_t loop_count;       // plays in all; 0 forever
    int32_t incomplete;       // JXL_ANIMATION_COMPLETE, _TRUNCATED or _BROKEN: why frames may be missing
    int32_t file_color_space; // 1: frames come in the file's own color space, which
                              // jxl_animation_icc describes; 0: in decode_jxl_from_memory's
} jxl_animation_info;

#define JXL_ANIMATION_COMPLETE 0
#define JXL_ANIMATION_TRUNCATED 1 // the data ends before the last frame's header
#define JXL_ANIMATION_BROKEN 2    // the stream is corrupt after the frames listed

JXL_NATIVE_API jxl_animation *jxl_animation_open(const uint8_t *data, size_t size, jxl_animation_info *out_info);

JXL_NATIVE_API bool jxl_animation_frame_durations(const jxl_animation *animation, int32_t *out_ms, int32_t count);

JXL_NATIVE_API bool jxl_animation_decode_frame(jxl_animation *animation, int32_t index, uint8_t **out_pixels, int32_t *out_partial);

JXL_NATIVE_API bool jxl_animation_icc(const jxl_animation *animation, const uint8_t **out_icc, size_t *out_size);

JXL_NATIVE_API void jxl_animation_close(jxl_animation *animation);

#ifdef __cplusplus
} // extern "C"
#endif
