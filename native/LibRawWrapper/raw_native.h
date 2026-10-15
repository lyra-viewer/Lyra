// -----------------------------------------------------------------------------
// raw_native — C ABI over LibRaw, consumed from managed code (Lyra.Imaging's
// RawNative). This header is the contract: the .cpp includes it so a signature
// change here is a compile error there, and the managed P/Invoke declarations
// mirror it by hand.
//
// Conventions shared by every Lyra native wrapper:
//   * Entry points are extern "C" and cdecl, and never let an exception escape.
//   * A `bool` return is the 1-byte C++/C bool both targeted ABIs use, which the
//     managed side marshals as UnmanagedType.I1.
//   * On failure the call returns false (or null), leaves every out-parameter
//     zeroed, and leaves a reason in get_last_raw_error().
//   * Buffers handed back are owned by the caller and released with the matching
//     free_* function - never with the platform free().
//   * Paths are UTF-8 on every platform.
// -----------------------------------------------------------------------------

#pragma once

#include <stddef.h>
#include <stdint.h>

#ifndef __cplusplus
#include <stdbool.h>
#endif

#ifdef _WIN32
#define RAW_NATIVE_API __declspec(dllexport)
#else
#define RAW_NATIVE_API __attribute__((visibility("default")))
#endif

#ifdef __cplusplus
extern "C" {
#endif

typedef struct raw_image raw_image;

#define RAW_SENSOR_BAYER 0
#define RAW_SENSOR_XTRANS 1
#define RAW_SENSOR_LINEAR 2     // already demosaiced (linear DNG)
#define RAW_SENSOR_MONOCHROME 3
#define RAW_SENSOR_OTHER 4

#define RAW_DAMAGE_NONE 0
#define RAW_DAMAGE_TRUNCATED 1 // the file ends before the raw data does
#define RAW_DAMAGE_CORRUPT 2   // LibRaw read past data it could not decode

#define RAW_PREVIEW_NONE 0
#define RAW_PREVIEW_JPEG 1
#define RAW_PREVIEW_RGB8 2

typedef struct {
    int32_t width;                // developed size, after orientation
    int32_t height;
    int32_t raw_width;
    int32_t raw_height;
    int32_t flip;                 // LibRaw's: 0 none, 3 180, 5 90 CCW, 6 90 CW
    int32_t sensor;               // RAW_SENSOR_*
    char cfa_pattern[8];          // e.g. "RGGB" for a 2x2 Bayer tile, else empty
    int32_t raw_bits;
    float as_shot_multipliers[4]; // R G B G2, as the camera recorded them
    float baseline_exposure;      // DNG BaselineExposure in stops, 0 when absent
    uint32_t dng_version;         // 0 for non-DNG, else 0xAABBCCDD
    int32_t preview_count;
    int32_t preview_width;        // the largest decodable preview, 0 when none
    int32_t preview_height;   
    int32_t preview_format;       // RAW_PREVIEW_* of that preview
    char make[64];
    char model[64];
    int32_t preview_color_space;  // DNG PreviewColorSpace: 0 unknown, 1 gray 2.2, 2 sRGB, 3 Adobe RGB, 4 ProPhoto
} raw_info;

typedef struct {
    int32_t half_size; // 1: half resolution per axis, no demosaic - fast
    int32_t demosaic;  // LibRaw user_qual: 0 linear, 1 VNG, 2 PPG, 3 AHD, 4 DCB, 11 DHT, 12 AAHD
} raw_develop_options;

typedef struct {
    int32_t width;
    int32_t height;
    const uint16_t *pixels;  // interleaved RGB, linear, 65535 = sensor saturation, sRGB primaries
    void *owner;             // release with free_raw_pixels
    int32_t black_level_min; // per-channel black, which many formats only reveal once unpacked
    int32_t black_level_max;
    int32_t white_level;
    int32_t damage; // RAW_DAMAGE_*: the pixels are complete but not all of them are what the camera recorded
} raw_pixels;

typedef struct {
    int32_t format; // RAW_PREVIEW_JPEG: data is a JPEG stream; RAW_PREVIEW_RGB8: interleaved RGB
    int32_t width;
    int32_t height;
    const uint8_t *data;
    size_t size;
    void *owner;    // release with free_raw_preview
} raw_preview;

RAW_NATIVE_API const char *get_last_raw_error(void);

RAW_NATIVE_API const char *raw_libraw_version(void);

RAW_NATIVE_API raw_image *raw_open_memory(const uint8_t *data, size_t size, raw_info *out_info);

RAW_NATIVE_API raw_image *raw_open_file(const char *utf8_path, raw_info *out_info);

RAW_NATIVE_API bool raw_extract_preview(raw_image *image, int32_t min_long_side, raw_preview *out_preview);

// One decodable embedded preview, as LibRaw lists it.
typedef struct {
    int32_t index;  // for raw_extract_preview_at
    int32_t format; // RAW_PREVIEW_*
    int32_t width;  // as stored: raw_info.flip applies
    int32_t height;
    int64_t size;   // bytes it takes in the file
} raw_preview_entry;

RAW_NATIVE_API int32_t raw_list_previews(raw_image *image, raw_preview_entry *out_entries, int32_t capacity);

RAW_NATIVE_API bool raw_extract_preview_at(raw_image *image, int32_t index, raw_preview *out_preview);

RAW_NATIVE_API void free_raw_preview(void *owner);

RAW_NATIVE_API bool raw_develop(raw_image *image, const raw_develop_options *options, raw_pixels *out_pixels);

RAW_NATIVE_API void free_raw_pixels(void *owner);

RAW_NATIVE_API void raw_cancel(raw_image *image);

RAW_NATIVE_API void raw_close(raw_image *image);

#ifdef __cplusplus
} // extern "C"
#endif
