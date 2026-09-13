// -----------------------------------------------------------------------------
// tiff_native — C ABI over libtiff, consumed from Lyra.Imaging's TiffNative,
// whose P/Invoke declarations mirror this header by hand.
//
// Conventions shared by every Lyra native wrapper:
//   * Entry points are extern "C" and cdecl, and never let an exception escape.
//   * A `bool` return is the 1-byte C++/C bool, marshalled as UnmanagedType.I1.
//   * On failure the call returns false, leaves every out-parameter zeroed, and
//     leaves a reason in get_last_tiff_error().
//   * Buffers handed back are released with the matching free_* function.
// -----------------------------------------------------------------------------

#pragma once

#include <stdint.h>

#ifndef __cplusplus
#include <stdbool.h>
#endif

// Always an export: the managed side loads the library and resolves symbols by
// name, so there is no import case.
#ifdef _WIN32
#define TIFF_API __declspec(dllexport)
#else
#define TIFF_API __attribute__((visibility("default")))
#endif

#ifdef __cplusplus
extern "C" {
#endif

// One directory of a TIFF, as enumerated by describe_tiff_directories. Field order is chosen so
// natural C alignment and .NET LayoutKind.Sequential agree without packing.
typedef struct TiffDirectoryInfo {
    uint32_t width;
    uint32_t height;
    uint32_t subfile_type;      // SUBFILETYPE bits as stored; 0 when the tag is absent
    uint32_t tile_width;        // 0 when the directory is stripped
    uint32_t tile_height;
    uint32_t rows_per_strip;    // 0 when the directory is tiled
    uint16_t page_number;       // from PAGENUMBER, 0 when absent
    uint16_t page_total;
    uint16_t bits_per_sample;
    uint16_t samples_per_pixel;
    uint16_t sample_format;     // SAMPLEFORMAT_UINT 1, INT 2, IEEEFP 3
    uint16_t planar_config;     // CONTIG 1, SEPARATE 2
    uint16_t photometric;
    uint16_t compression;
    uint8_t  is_tiled;
    uint8_t  gray_capable;      // non-zero => load_tiff_gray_region can read this directory
    uint8_t  rgba_capable;      // non-zero => libtiff's RGBA interface will read it
    uint8_t  native_capable;    // non-zero => load_tiff_native can read it
    uint8_t  region_capable;    // non-zero => load_tiff_*_region can read it by rectangle
    uint8_t  region_samples;    // bytes per pixel a region comes back as: 1 grey, 4 RGBA
    uint8_t  region_premul;     // non-zero => a colour region's alpha is associated
} TiffDirectoryInfo;            // sizeof 48 (1 byte tail padding), alignof 4

TIFF_API const char *get_last_tiff_error(void);

TIFF_API bool describe_tiff_directories(const char *path, TiffDirectoryInfo **out_dirs, int *out_count);

TIFF_API bool describe_tiff_directories_mem(const uint8_t *data, uint64_t size, TiffDirectoryInfo **out_dirs, int *out_count);

TIFF_API void free_tiff_directories(TiffDirectoryInfo *ptr);

TIFF_API bool load_tiff_rgba(const char *path, uint8_t **out_pixels, int *width, int *height, uint8_t **out_icc, int *out_icc_size);

TIFF_API bool load_tiff_rgba_mem(const uint8_t *data, uint64_t size, uint8_t **out_pixels, int *width, int *height, uint8_t **out_icc, int *out_icc_size);

// The same decode, from one named directory. Directory 0 is what the two functions above read.
TIFF_API bool load_tiff_rgba_at(const char *path, int directory, uint8_t **out_pixels, int *width, int *height, uint8_t **out_icc, int *out_icc_size);

TIFF_API bool load_tiff_rgba_mem_at(const uint8_t *data, uint64_t size, int directory, uint8_t **out_pixels,
                                    int *width, int *height, uint8_t **out_icc, int *out_icc_size);

TIFF_API bool load_tiff_gray_region(const char *path, int directory, uint32_t x, uint32_t y, uint32_t width, uint32_t height,
                                    uint8_t **out_pixels, uint32_t *out_stride);

TIFF_API bool load_tiff_rgba_region(const char *path, int directory,
                                    uint32_t x, uint32_t y, uint32_t width, uint32_t height,
                                    uint8_t **out_pixels, uint32_t *out_stride);

typedef enum TiffOutputKind {
    TIFF_OUT_GRAY8 = 0,    // one byte per pixel
    TIFF_OUT_RGBA8 = 1,    // four bytes per pixel
    TIFF_OUT_RGBA_F32 = 2  // four floats per pixel, values as the file holds them
} TiffOutputKind;

TIFF_API bool load_tiff_native(const char *path, int directory, int output_kind, uint8_t **out_pixels,
                               int *out_width, int *out_height, uint32_t *out_stride);

TIFF_API void free_tiff_pixels(uint8_t *ptr);

#ifdef __cplusplus
} // extern "C"
#endif
