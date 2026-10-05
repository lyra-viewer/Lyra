#pragma once

#include <cstddef>
#include <cstdint>

#include <jxl/decode.h>

namespace jxl_internal {

void set_error(const char *fmt, ...);
void clear_error();
const char *last_error();

// Every buffer handed to the caller is tracked, so free_jxl_pixels frees only its own.
void track_allocation(void *ptr);
bool release_allocation(void *ptr);

// The CMS, the parallel runner when there is one, and straight alpha: Lyra premultiplies
// itself, so libjxl must not hand back colors it already premultiplied.
bool configure_decoder(JxlDecoder *dec, void *runner);

// HDR (float) stays linear scene-referred for the tone mapper; SDR converts to
// Display-P3 (sRGB transfer, P3 primaries, D65) instead of clamping to sRGB.
bool set_output_profile(JxlDecoder *dec, bool is_hdr);

// HDR == floating-point sample type. A 16-bit *integer* image is still SDR.
bool is_hdr_image(const JxlBasicInfo &info);

JxlPixelFormat pixel_format(bool is_hdr);

} // namespace jxl_internal
