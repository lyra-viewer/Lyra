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
    Rgba8Unorm,
    Rgba8UnormSrgb,
    Rgba8Snorm,
    Bgra8Unorm,
    Bgra8UnormSrgb,

    // Uncompressed float (also the decode target for HDR block formats).
    Rgba16Float,
    Rgba32Float,

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
}