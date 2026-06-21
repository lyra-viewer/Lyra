namespace Lyra.ManagedCodecs.Texture;

/// <summary>
/// The canonical ASTC footprint ordering shared by every container's format mapping. Footprints are
/// listed in the order all three enum spaces use (OpenGL <c>0x93B0…</c>, Vulkan <c>157…</c>, and
/// DXGI <c>134…</c>), so a footprint index plus an sRGB flag resolves to the neutral format.
/// </summary>
internal static class AstcFormats
{
    public static readonly string[] Footprints = ["4x4", "5x4", "5x5", "6x5", "6x6", "8x5", "8x6", "8x8", "10x5", "10x6", "10x8", "10x10", "12x10", "12x12"];

    private static readonly TextureFormat[] Ldr =
    [
        TextureFormat.Astc4x4Unorm, TextureFormat.Astc4x4UnormSrgb,
        TextureFormat.Astc5x4Unorm, TextureFormat.Astc5x4UnormSrgb,
        TextureFormat.Astc5x5Unorm, TextureFormat.Astc5x5UnormSrgb,
        TextureFormat.Astc6x5Unorm, TextureFormat.Astc6x5UnormSrgb,
        TextureFormat.Astc6x6Unorm, TextureFormat.Astc6x6UnormSrgb,
        TextureFormat.Astc8x5Unorm, TextureFormat.Astc8x5UnormSrgb,
        TextureFormat.Astc8x6Unorm, TextureFormat.Astc8x6UnormSrgb,
        TextureFormat.Astc8x8Unorm, TextureFormat.Astc8x8UnormSrgb,
        TextureFormat.Astc10x5Unorm, TextureFormat.Astc10x5UnormSrgb,
        TextureFormat.Astc10x6Unorm, TextureFormat.Astc10x6UnormSrgb,
        TextureFormat.Astc10x8Unorm, TextureFormat.Astc10x8UnormSrgb,
        TextureFormat.Astc10x10Unorm, TextureFormat.Astc10x10UnormSrgb,
        TextureFormat.Astc12x10Unorm, TextureFormat.Astc12x10UnormSrgb,
        TextureFormat.Astc12x12Unorm, TextureFormat.Astc12x12UnormSrgb,
    ];

    public static int Count => Footprints.Length;

    /// <summary>The LDR format for a footprint index (0-13) and sRGB-ness.</summary>
    public static TextureFormat LdrFormat(int footprintIndex, bool srgb) 
        => Ldr[(footprintIndex * 2) + (srgb ? 1 : 0)];

    /// <summary>
    /// The 3D ASTC footprints, in the OpenGL OES ordering (<c>GL_..._ASTC_3x3x3_OES</c> = 0x93C0 …
    /// <c>6x6x6</c> = 0x93C9, with the sRGB block at 0x93E0…0x93E9).
    /// </summary>
    public static readonly string[] Footprints3D = ["3x3x3", "4x3x3", "4x4x3", "4x4x4", "5x4x4", "5x5x4", "5x5x5", "6x5x5", "6x6x5", "6x6x6"];

    private static readonly TextureFormat[] Ldr3D =
    [
        TextureFormat.Astc3x3x3Unorm, TextureFormat.Astc3x3x3UnormSrgb,
        TextureFormat.Astc4x3x3Unorm, TextureFormat.Astc4x3x3UnormSrgb,
        TextureFormat.Astc4x4x3Unorm, TextureFormat.Astc4x4x3UnormSrgb,
        TextureFormat.Astc4x4x4Unorm, TextureFormat.Astc4x4x4UnormSrgb,
        TextureFormat.Astc5x4x4Unorm, TextureFormat.Astc5x4x4UnormSrgb,
        TextureFormat.Astc5x5x4Unorm, TextureFormat.Astc5x5x4UnormSrgb,
        TextureFormat.Astc5x5x5Unorm, TextureFormat.Astc5x5x5UnormSrgb,
        TextureFormat.Astc6x5x5Unorm, TextureFormat.Astc6x5x5UnormSrgb,
        TextureFormat.Astc6x6x5Unorm, TextureFormat.Astc6x6x5UnormSrgb,
        TextureFormat.Astc6x6x6Unorm, TextureFormat.Astc6x6x6UnormSrgb,
    ];

    public static int Count3D => Footprints3D.Length;

    /// <summary>The 3D LDR format for a footprint index (0-9) and sRGB-ness.</summary>
    public static TextureFormat Ldr3DFormat(int footprintIndex, bool srgb) 
        => Ldr3D[(footprintIndex * 2) + (srgb ? 1 : 0)];
}