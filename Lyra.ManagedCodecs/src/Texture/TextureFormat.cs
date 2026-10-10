namespace Lyra.ManagedCodecs.Texture;

/// <summary>
/// The neutral GPU texture format identity that every container (DDS, KTX, …) maps into and every
/// decoder maps out of. sRGB-ness and signedness are part of the identity, not side metadata.
///
/// Only formats with a working decode path are listed; members are added as support lands. This
/// keeps "recognized but unimplemented" from masquerading as a real format.
/// </summary>
public enum TextureFormat
{
    Unknown = 0,

    // Uncompressed 8-bit.
    R8Unorm,
    R8UnormSrgb,
    R8Snorm,
    A8Unorm,            // alpha only; shown as white with that alpha
    La8Unorm,           // luminance, then alpha
    Rg8Unorm,
    Rgb8Unorm,
    Rgb8UnormSrgb,
    Bgr8Unorm,
    Bgr8UnormSrgb,
    Rgba8Unorm,
    Rgba8UnormSrgb,
    Rgba8Snorm,
    Rgbx8Unorm,         // the fourth byte is unused padding
    Bgra8Unorm,
    Bgra8UnormSrgb,
    Bgrx8Unorm,
    Bgrx8UnormSrgb,

    // Uncompressed 16-bit single channel.
    R16Unorm,

    // Uncompressed 8-bit integer (non-normalized). Shown as grayscale: unsigned passes through,
    // signed uses the same [-1,1]->[0,255] remap as the snorm path.
    R8Uint,
    R8Sint,

    // Packed integer (decoded to RGBA8). Components are R-first in the high bits, per GL/Vulkan PACK.
    Rgba4Unorm,         // R4G4B4A4
    Rgb5A1Unorm,        // R5G5B5A1
    Rgb565Unorm,        // R5G6B5
    Rgb10A2Unorm,       // A2B10G10R10

    // Packed integer, B in the low bits: DXGI's B5G5R5A1 / B4G4R4A4, D3D9's A1R5G5B5 / A4R4G4B4.
    Bgr5A1Unorm,
    Bgr5X1Unorm,        // the top bit is unused
    Bgra4Unorm,

    // Uncompressed float (also the decode target for HDR block formats).
    Rgba16Float,
    Rgba32Float,
    R16Float,
    R32Float,
    Rg16Float,
    Rg32Float,
    Rgb16Float,
    B10G11R11UFloat,    // packed: 11-bit R, 11-bit G, 10-bit B unsigned floats
    Rgb9E5UFloat,       // packed: 3x 9-bit mantissa + shared 5-bit exponent

    // Block-compressed (BCn / DXT).
    Bc1RgbaUnorm,
    Bc1RgbaUnormSrgb,
    Bc2Unorm,
    Bc2UnormSrgb,
    Bc3Unorm,
    Bc3UnormSrgb,
    Bc4Unorm,
    Bc4Snorm,
    Bc5Unorm,
    Bc5Snorm,
    Bc7Unorm,
    Bc7UnormSrgb,
    Bc6HUFloat,
    Bc6HSFloat,

    // Block-compressed (ETC2 / EAC).
    Etc2Rgb8Unorm,
    Etc2Rgb8UnormSrgb,
    Etc2Rgb8A1Unorm,
    Etc2Rgb8A1UnormSrgb,
    Etc2Rgba8Unorm,
    Etc2Rgba8UnormSrgb,
    EacR11Unorm,
    EacR11Snorm,
    EacRg11Unorm,
    EacRg11Snorm,

    // Block-compressed (ASTC LDR). Footprint is part of the identity, so every block size is its own
    // format - exactly as Vulkan enumerates them. All footprints share a 128-bit (16-byte) block.
    Astc4x4Unorm,
    Astc4x4UnormSrgb,
    Astc5x4Unorm,
    Astc5x4UnormSrgb,
    Astc5x5Unorm,
    Astc5x5UnormSrgb,
    Astc6x5Unorm,
    Astc6x5UnormSrgb,
    Astc6x6Unorm,
    Astc6x6UnormSrgb,
    Astc8x5Unorm,
    Astc8x5UnormSrgb,
    Astc8x6Unorm,
    Astc8x6UnormSrgb,
    Astc8x8Unorm,
    Astc8x8UnormSrgb,
    Astc10x5Unorm,
    Astc10x5UnormSrgb,
    Astc10x6Unorm,
    Astc10x6UnormSrgb,
    Astc10x8Unorm,
    Astc10x8UnormSrgb,
    Astc10x10Unorm,
    Astc10x10UnormSrgb,
    Astc12x10Unorm,
    Astc12x10UnormSrgb,
    Astc12x12Unorm,
    Astc12x12UnormSrgb,

    // Block-compressed (ASTC 3D LDR). Volume footprints from the OES extension; each 128-bit block
    // covers blockW×blockH×blockD texels. Only KTX1 (GL) carries these - core Vulkan has no 3D ASTC.
    Astc3x3x3Unorm,
    Astc3x3x3UnormSrgb,
    Astc4x3x3Unorm,
    Astc4x3x3UnormSrgb,
    Astc4x4x3Unorm,
    Astc4x4x3UnormSrgb,
    Astc4x4x4Unorm,
    Astc4x4x4UnormSrgb,
    Astc5x4x4Unorm,
    Astc5x4x4UnormSrgb,
    Astc5x5x4Unorm,
    Astc5x5x4UnormSrgb,
    Astc5x5x5Unorm,
    Astc5x5x5UnormSrgb,
    Astc6x5x5Unorm,
    Astc6x5x5UnormSrgb,
    Astc6x6x5Unorm,
    Astc6x6x5UnormSrgb,
    Astc6x6x6Unorm,
    Astc6x6x6UnormSrgb,
}