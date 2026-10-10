namespace Lyra.ManagedCodecs.Texture.Ktx;

/// <summary>
/// Maps the two enum spaces Khronos Texture files use - OpenGL <c>glInternalFormat</c> (KTX 1.x) and
/// Vulkan <c>VkFormat</c> (KTX 2.0) - onto the neutral <see cref="TextureFormat"/>, and supplies the
/// canonical names shown in the inspector. Only formats with a working decode path are mapped;
/// everything else returns <see cref="TextureFormat.Unknown"/> so the reader can raise a precise
/// "recognized but unsupported" error rather than guessing.
/// </summary>
internal static class KtxFormatMap
{
    // ------------------------------------------------------------------
    //  KTX 1.x - OpenGL glInternalFormat
    // ------------------------------------------------------------------

    public static TextureFormat FromGl(uint glInternalFormat) => glInternalFormat switch
    {
        0x8229 => TextureFormat.R8Unorm,             // GL_R8
        0x8F94 => TextureFormat.R8Snorm,             // GL_R8_SNORM
        0x8232 => TextureFormat.R8Uint,              // GL_R8UI
        0x8231 => TextureFormat.R8Sint,              // GL_R8I
        0x822A => TextureFormat.R16Unorm,            // GL_R16
        0x822B => TextureFormat.Rg8Unorm,            // GL_RG8
        0x8051 => TextureFormat.Rgb8Unorm,           // GL_RGB8
        0x8C41 => TextureFormat.Rgb8UnormSrgb,       // GL_SRGB8
        0x8056 => TextureFormat.Rgba4Unorm,          // GL_RGBA4
        0x8057 => TextureFormat.Rgb5A1Unorm,         // GL_RGB5_A1
        0x8D62 => TextureFormat.Rgb565Unorm,         // GL_RGB565
        0x8059 => TextureFormat.Rgb10A2Unorm,        // GL_RGB10_A2
        0x8058 => TextureFormat.Rgba8Unorm,          // GL_RGBA8
        0x8C43 => TextureFormat.Rgba8UnormSrgb,      // GL_SRGB8_ALPHA8
        0x8F97 => TextureFormat.Rgba8Snorm,          // GL_RGBA8_SNORM
        0x881A => TextureFormat.Rgba16Float,         // GL_RGBA16F
        0x8814 => TextureFormat.Rgba32Float,         // GL_RGBA32F
        0x822D => TextureFormat.R16Float,            // GL_R16F
        0x822E => TextureFormat.R32Float,            // GL_R32F
        0x881B => TextureFormat.Rgb16Float,          // GL_RGB16F
        0x8C3A => TextureFormat.B10G11R11UFloat,     // GL_R11F_G11F_B10F
        0x8C3D => TextureFormat.Rgb9E5UFloat,        // GL_RGB9_E5
     
        0x83F0 => TextureFormat.Bc1RgbaUnorm,        // GL_COMPRESSED_RGB_S3TC_DXT1_EXT
        0x83F1 => TextureFormat.Bc1RgbaUnorm,        // GL_COMPRESSED_RGBA_S3TC_DXT1_EXT
        0x8C4C => TextureFormat.Bc1RgbaUnormSrgb,    // GL_COMPRESSED_SRGB_S3TC_DXT1_EXT
        0x8C4D => TextureFormat.Bc1RgbaUnormSrgb,    // GL_COMPRESSED_SRGB_ALPHA_S3TC_DXT1_EXT
        0x83F2 => TextureFormat.Bc2Unorm,            // GL_COMPRESSED_RGBA_S3TC_DXT3_EXT
        0x8C4E => TextureFormat.Bc2UnormSrgb,        // GL_COMPRESSED_SRGB_ALPHA_S3TC_DXT3_EXT
        0x83F3 => TextureFormat.Bc3Unorm,            // GL_COMPRESSED_RGBA_S3TC_DXT5_EXT
        0x8C4F => TextureFormat.Bc3UnormSrgb,        // GL_COMPRESSED_SRGB_ALPHA_S3TC_DXT5_EXT
        0x8DBB => TextureFormat.Bc4Unorm,            // GL_COMPRESSED_RED_RGTC1
        0x8DBC => TextureFormat.Bc4Snorm,            // GL_COMPRESSED_SIGNED_RED_RGTC1
        0x8DBD => TextureFormat.Bc5Unorm,            // GL_COMPRESSED_RG_RGTC2
        0x8DBE => TextureFormat.Bc5Snorm,            // GL_COMPRESSED_SIGNED_RG_RGTC2
        0x8E8C => TextureFormat.Bc7Unorm,            // GL_COMPRESSED_RGBA_BPTC_UNORM
        0x8E8D => TextureFormat.Bc7UnormSrgb,        // GL_COMPRESSED_SRGB_ALPHA_BPTC_UNORM
        0x8E8E => TextureFormat.Bc6HSFloat,          // GL_COMPRESSED_RGB_BPTC_SIGNED_FLOAT
        0x8E8F => TextureFormat.Bc6HUFloat,          // GL_COMPRESSED_RGB_BPTC_UNSIGNED_FLOAT

        0x8D64 => TextureFormat.Etc2Rgb8Unorm,       // GL_ETC1_RGB8_OES (ETC1 is an ETC2 RGB subset)
        0x9274 => TextureFormat.Etc2Rgb8Unorm,       // GL_COMPRESSED_RGB8_ETC2
        0x9275 => TextureFormat.Etc2Rgb8UnormSrgb,   // GL_COMPRESSED_SRGB8_ETC2
        0x9276 => TextureFormat.Etc2Rgb8A1Unorm,     // GL_COMPRESSED_RGB8_PUNCHTHROUGH_ALPHA1_ETC2
        0x9277 => TextureFormat.Etc2Rgb8A1UnormSrgb, // GL_COMPRESSED_SRGB8_PUNCHTHROUGH_ALPHA1_ETC2
        0x9278 => TextureFormat.Etc2Rgba8Unorm,      // GL_COMPRESSED_RGBA8_ETC2_EAC
        0x9279 => TextureFormat.Etc2Rgba8UnormSrgb,  // GL_COMPRESSED_SRGB8_ALPHA8_ETC2_EAC
        0x9270 => TextureFormat.EacR11Unorm,         // GL_COMPRESSED_R11_EAC
        0x9271 => TextureFormat.EacR11Snorm,         // GL_COMPRESSED_SIGNED_R11_EAC
        0x9272 => TextureFormat.EacRg11Unorm,        // GL_COMPRESSED_RG11_EAC
        0x9273 => TextureFormat.EacRg11Snorm,        // GL_COMPRESSED_SIGNED_RG11_EAC

        // GL_COMPRESSED_RGBA_ASTC_*x*_KHR (LDR) and the SRGB8_ALPHA8 block.
        0x93B0 => TextureFormat.Astc4x4Unorm,
        0x93B1 => TextureFormat.Astc5x4Unorm,
        0x93B2 => TextureFormat.Astc5x5Unorm,
        0x93B3 => TextureFormat.Astc6x5Unorm,
        0x93B4 => TextureFormat.Astc6x6Unorm,
        0x93B5 => TextureFormat.Astc8x5Unorm,
        0x93B6 => TextureFormat.Astc8x6Unorm,
        0x93B7 => TextureFormat.Astc8x8Unorm,
        0x93B8 => TextureFormat.Astc10x5Unorm,
        0x93B9 => TextureFormat.Astc10x6Unorm,
        0x93BA => TextureFormat.Astc10x8Unorm,
        0x93BB => TextureFormat.Astc10x10Unorm,
        0x93BC => TextureFormat.Astc12x10Unorm,
        0x93BD => TextureFormat.Astc12x12Unorm,
        0x93D0 => TextureFormat.Astc4x4UnormSrgb,
        0x93D1 => TextureFormat.Astc5x4UnormSrgb,
        0x93D2 => TextureFormat.Astc5x5UnormSrgb,
        0x93D3 => TextureFormat.Astc6x5UnormSrgb,
        0x93D4 => TextureFormat.Astc6x6UnormSrgb,
        0x93D5 => TextureFormat.Astc8x5UnormSrgb,
        0x93D6 => TextureFormat.Astc8x6UnormSrgb,
        0x93D7 => TextureFormat.Astc8x8UnormSrgb,
        0x93D8 => TextureFormat.Astc10x5UnormSrgb,
        0x93D9 => TextureFormat.Astc10x6UnormSrgb,
        0x93DA => TextureFormat.Astc10x8UnormSrgb,
        0x93DB => TextureFormat.Astc10x10UnormSrgb,
        0x93DC => TextureFormat.Astc12x10UnormSrgb,
        0x93DD => TextureFormat.Astc12x12UnormSrgb,

        // GL_COMPRESSED_RGBA_ASTC_*x*x*_OES (3D LDR) and the SRGB8_ALPHA8 3D block.
        >= 0x93C0 and <= 0x93C9 => AstcFormats.Ldr3DFormat((int)(glInternalFormat - 0x93C0), srgb: false),
        >= 0x93E0 and <= 0x93E9 => AstcFormats.Ldr3DFormat((int)(glInternalFormat - 0x93E0), srgb: true),
        _ => TextureFormat.Unknown,
    };

    // glFormat and glType values that describe uncompressed pixel data.
    private const uint GlRed = 0x1903, GlRg = 0x8227, GlRgb = 0x1907, GlRgba = 0x1908, GlBgr = 0x80E0, GlBgra = 0x80E1;
    private const uint GlAlpha = 0x1906, GlLuminance = 0x1909, GlLuminanceAlpha = 0x190A, GlRedInteger = 0x8D94;

    private const uint GlByte = 0x1400, GlUnsignedByte = 0x1401, GlUnsignedShort = 0x1403, GlFloat = 0x1406;
    private const uint GlHalfFloat = 0x140B, GlHalfFloatOes = 0x8D61;
    private const uint GlUnsignedShort4444 = 0x8033, GlUnsignedShort5551 = 0x8034, GlUnsignedShort565 = 0x8363;
    private const uint GlUnsignedInt2101010Rev = 0x8368, GlUnsignedInt10F11F11FRev = 0x8C3B, GlUnsignedInt5999Rev = 0x8C3E;

    /// <summary>
    /// The format of uncompressed KTX 1 data, from the <c>glFormat</c> and <c>glType</c> that
    /// describe the bytes; <c>glInternalFormat</c> only asks a GPU how to store them, and some
    /// writers leave it at GL_RGBA8 for float data. It still says whether 8-bit data is sRGB.
    /// Unknown for a pair this does not cover, leaving the internal format to decide.
    /// </summary>
    public static TextureFormat FromGlData(uint glFormat, uint glType, uint glInternalFormat)
    {
        var srgb = glInternalFormat is 0x8C40 or 0x8C41 or 0x8C42 or 0x8C43; // GL_SRGB, GL_SRGB8, GL_SRGB_ALPHA, GL_SRGB8_ALPHA8
        var half = glType is GlHalfFloat or GlHalfFloatOes;

        return (glFormat, glType) switch
        {
            (GlRed or GlLuminance, GlUnsignedByte) => srgb ? TextureFormat.R8UnormSrgb : TextureFormat.R8Unorm,
            (GlRed, GlByte) => TextureFormat.R8Snorm,
            (GlRedInteger, GlUnsignedByte) => TextureFormat.R8Uint,
            (GlRedInteger, GlByte) => TextureFormat.R8Sint,
            (GlAlpha, GlUnsignedByte) => TextureFormat.A8Unorm,
            (GlLuminanceAlpha, GlUnsignedByte) => TextureFormat.La8Unorm,
            (GlRg, GlUnsignedByte) => TextureFormat.Rg8Unorm,
            (GlRgb, GlUnsignedByte) => srgb ? TextureFormat.Rgb8UnormSrgb : TextureFormat.Rgb8Unorm,
            (GlBgr, GlUnsignedByte) => srgb ? TextureFormat.Bgr8UnormSrgb : TextureFormat.Bgr8Unorm,
            (GlRgba, GlUnsignedByte) => srgb ? TextureFormat.Rgba8UnormSrgb : TextureFormat.Rgba8Unorm,
            (GlRgba, GlByte) => TextureFormat.Rgba8Snorm,
            (GlBgra, GlUnsignedByte) => srgb ? TextureFormat.Bgra8UnormSrgb : TextureFormat.Bgra8Unorm,
            (GlRed, GlUnsignedShort) => TextureFormat.R16Unorm,
            (GlRed, _) when half => TextureFormat.R16Float,
            (GlRg, _) when half => TextureFormat.Rg16Float,
            (GlRgb, _) when half => TextureFormat.Rgb16Float,
            (GlRgba, _) when half => TextureFormat.Rgba16Float,
            (GlRed, GlFloat) => TextureFormat.R32Float,
            (GlRg, GlFloat) => TextureFormat.Rg32Float,
            (GlRgba, GlFloat) => TextureFormat.Rgba32Float,
            (GlRgba, GlUnsignedShort4444) => TextureFormat.Rgba4Unorm,
            (GlRgba, GlUnsignedShort5551) => TextureFormat.Rgb5A1Unorm,
            (GlRgb, GlUnsignedShort565) => TextureFormat.Rgb565Unorm,
            (GlRgba, GlUnsignedInt2101010Rev) => TextureFormat.Rgb10A2Unorm,
            (GlRgb, GlUnsignedInt10F11F11FRev) => TextureFormat.B10G11R11UFloat,
            (GlRgb, GlUnsignedInt5999Rev) => TextureFormat.Rgb9E5UFloat,
            _ => TextureFormat.Unknown,
        };
    }

    /// <summary>How the inspector names uncompressed data whose bytes differ from its internal format.</summary>
    public static string GlDataName(uint glFormat, uint glType)
    {
        var format = glFormat switch
        {
            GlRed            => "GL_RED", 
            GlRg             => "GL_RG", 
            GlRgb            => "GL_RGB",
            GlRgba           => "GL_RGBA",
            GlBgr            => "GL_BGR", 
            GlBgra           => "GL_BGRA",
            GlAlpha          => "GL_ALPHA", 
            GlLuminance      => "GL_LUMINANCE", 
            GlLuminanceAlpha => "GL_LUMINANCE_ALPHA", 
            GlRedInteger     => "GL_RED_INTEGER",
            _ => $"0x{glFormat:X}"
        };

        var type = glType switch
        {
            GlByte          => "GL_BYTE", 
            GlUnsignedByte  => "GL_UNSIGNED_BYTE", 
            GlUnsignedShort => "GL_UNSIGNED_SHORT", 
            GlFloat         => "GL_FLOAT",
            GlHalfFloat or GlHalfFloatOes => "GL_HALF_FLOAT",
            GlUnsignedShort4444       => "GL_UNSIGNED_SHORT_4_4_4_4", 
            GlUnsignedShort5551       => "GL_UNSIGNED_SHORT_5_5_5_1", 
            GlUnsignedShort565        => "GL_UNSIGNED_SHORT_5_6_5",
            GlUnsignedInt2101010Rev   => "GL_UNSIGNED_INT_2_10_10_10_REV", 
            GlUnsignedInt10F11F11FRev => "GL_UNSIGNED_INT_10F_11F_11F_REV", 
            GlUnsignedInt5999Rev      => "GL_UNSIGNED_INT_5_9_9_9_REV",
            _ => $"0x{glType:X}"
        };

        return $"{format} / {type}";
    }

    public static string GlName(uint glInternalFormat) => glInternalFormat switch
    {
        0x8229 => "GL_R8",
        0x8F94 => "GL_R8_SNORM",
        0x8232 => "GL_R8UI",
        0x8231 => "GL_R8I",
        0x822A => "GL_R16",
        0x822B => "GL_RG8",
        0x8051 => "GL_RGB8",
        0x8C41 => "GL_SRGB8",
        0x8056 => "GL_RGBA4",
        0x8057 => "GL_RGB5_A1",
        0x8D62 => "GL_RGB565",
        0x8059 => "GL_RGB10_A2",
        0x8058 => "GL_RGBA8",
        0x8C43 => "GL_SRGB8_ALPHA8",
        0x8F97 => "GL_RGBA8_SNORM",
        0x881A => "GL_RGBA16F",
        0x8814 => "GL_RGBA32F",
        0x822D => "GL_R16F",
        0x822E => "GL_R32F",
        0x881B => "GL_RGB16F",
        0x8C3A => "GL_R11F_G11F_B10F",
        0x8C3D => "GL_RGB9_E5",
        0x83F0 => "GL_COMPRESSED_RGB_S3TC_DXT1_EXT",
        0x83F1 => "GL_COMPRESSED_RGBA_S3TC_DXT1_EXT",
        0x8C4C => "GL_COMPRESSED_SRGB_S3TC_DXT1_EXT",
        0x8C4D => "GL_COMPRESSED_SRGB_ALPHA_S3TC_DXT1_EXT",
        0x83F2 => "GL_COMPRESSED_RGBA_S3TC_DXT3_EXT",
        0x8C4E => "GL_COMPRESSED_SRGB_ALPHA_S3TC_DXT3_EXT",
        0x83F3 => "GL_COMPRESSED_RGBA_S3TC_DXT5_EXT",
        0x8C4F => "GL_COMPRESSED_SRGB_ALPHA_S3TC_DXT5_EXT",
        0x8DBB => "GL_COMPRESSED_RED_RGTC1",
        0x8DBC => "GL_COMPRESSED_SIGNED_RED_RGTC1",
        0x8DBD => "GL_COMPRESSED_RG_RGTC2",
        0x8DBE => "GL_COMPRESSED_SIGNED_RG_RGTC2",
        0x8E8C => "GL_COMPRESSED_RGBA_BPTC_UNORM",
        0x8E8D => "GL_COMPRESSED_SRGB_ALPHA_BPTC_UNORM",
        0x8E8E => "GL_COMPRESSED_RGB_BPTC_SIGNED_FLOAT",
        0x8E8F => "GL_COMPRESSED_RGB_BPTC_UNSIGNED_FLOAT",
        0x8D64 => "GL_ETC1_RGB8_OES",
        0x9274 => "GL_COMPRESSED_RGB8_ETC2",
        0x9275 => "GL_COMPRESSED_SRGB8_ETC2",
        0x9276 => "GL_COMPRESSED_RGB8_PUNCHTHROUGH_ALPHA1_ETC2",
        0x9277 => "GL_COMPRESSED_SRGB8_PUNCHTHROUGH_ALPHA1_ETC2",
        0x9278 => "GL_COMPRESSED_RGBA8_ETC2_EAC",
        0x9279 => "GL_COMPRESSED_SRGB8_ALPHA8_ETC2_EAC",
        0x9270 => "GL_COMPRESSED_R11_EAC",
        0x9271 => "GL_COMPRESSED_SIGNED_R11_EAC",
        0x9272 => "GL_COMPRESSED_RG11_EAC",
        0x9273 => "GL_COMPRESSED_SIGNED_RG11_EAC",
        >= 0x93B0 and <= 0x93BD => $"GL_COMPRESSED_RGBA_ASTC_{AstcFormats.Footprints[glInternalFormat - 0x93B0]}_KHR",
        >= 0x93D0 and <= 0x93DD => $"GL_COMPRESSED_SRGB8_ALPHA8_ASTC_{AstcFormats.Footprints[glInternalFormat - 0x93D0]}_KHR",
        >= 0x93C0 and <= 0x93C9 => $"GL_COMPRESSED_RGBA_ASTC_{AstcFormats.Footprints3D[glInternalFormat - 0x93C0]}_OES",
        >= 0x93E0 and <= 0x93E9 => $"GL_COMPRESSED_SRGB8_ALPHA8_ASTC_{AstcFormats.Footprints3D[glInternalFormat - 0x93E0]}_OES",
        _ => $"glInternalFormat 0x{glInternalFormat:X}",
    };

    /// <summary>
    /// Names an unmapped GL internal format, tagging the families we deliberately don't decode yet
    /// (PVRTC, 3D ASTC) so the reader's error is informative rather than a bare hex code.
    /// </summary>
    public static string DescribeUnsupportedGl(uint glInternalFormat) => glInternalFormat switch
    {
        (>= 0x8C00 and <= 0x8C03) or (>= 0x8A54 and <= 0x8A57) or (>= 0x9137 and <= 0x9138) or (>= 0x93F0 and <= 0x93F3)
            => $"glInternalFormat 0x{glInternalFormat:X} (PVRTC — not supported)",
        _ => GlName(glInternalFormat),
    };

    // ------------------------------------------------------------------
    //  KTX 2.0 - Vulkan VkFormat
    // ------------------------------------------------------------------

    public static TextureFormat FromVk(uint vkFormat) => vkFormat switch
    {
        2 => TextureFormat.Rgba4Unorm,            // VK_FORMAT_R4G4B4A4_UNORM_PACK16
        4 => TextureFormat.Rgb565Unorm,           // VK_FORMAT_R5G6B5_UNORM_PACK16
        6 => TextureFormat.Rgb5A1Unorm,           // VK_FORMAT_R5G5B5A1_UNORM_PACK16
        8 => TextureFormat.Bgr5A1Unorm,           // VK_FORMAT_A1R5G5B5_UNORM_PACK16
        9 => TextureFormat.R8Unorm,               // VK_FORMAT_R8_UNORM
        10 => TextureFormat.R8Snorm,              // VK_FORMAT_R8_SNORM
        13 => TextureFormat.R8Uint,               // VK_FORMAT_R8_UINT
        14 => TextureFormat.R8Sint,               // VK_FORMAT_R8_SINT
        15 => TextureFormat.R8UnormSrgb,          // VK_FORMAT_R8_SRGB
        16 => TextureFormat.Rg8Unorm,             // VK_FORMAT_R8G8_UNORM
        23 => TextureFormat.Rgb8Unorm,            // VK_FORMAT_R8G8B8_UNORM
        29 => TextureFormat.Rgb8UnormSrgb,        // VK_FORMAT_R8G8B8_SRGB
        30 => TextureFormat.Bgr8Unorm,            // VK_FORMAT_B8G8R8_UNORM
        36 => TextureFormat.Bgr8UnormSrgb,        // VK_FORMAT_B8G8R8_SRGB
        64 => TextureFormat.Rgb10A2Unorm,         // VK_FORMAT_A2B10G10R10_UNORM_PACK32
        70 => TextureFormat.R16Unorm,             // VK_FORMAT_R16_UNORM
        37 => TextureFormat.Rgba8Unorm,           // VK_FORMAT_R8G8B8A8_UNORM
        38 => TextureFormat.Rgba8Snorm,           // VK_FORMAT_R8G8B8A8_SNORM
        43 => TextureFormat.Rgba8UnormSrgb,       // VK_FORMAT_R8G8B8A8_SRGB
        44 => TextureFormat.Bgra8Unorm,           // VK_FORMAT_B8G8R8A8_UNORM
        50 => TextureFormat.Bgra8UnormSrgb,       // VK_FORMAT_B8G8R8A8_SRGB
        76 => TextureFormat.R16Float,             // VK_FORMAT_R16_SFLOAT
        83 => TextureFormat.Rg16Float,            // VK_FORMAT_R16G16_SFLOAT
        90 => TextureFormat.Rgb16Float,           // VK_FORMAT_R16G16B16_SFLOAT
        97 => TextureFormat.Rgba16Float,          // VK_FORMAT_R16G16B16A16_SFLOAT
        100 => TextureFormat.R32Float,            // VK_FORMAT_R32_SFLOAT
        103 => TextureFormat.Rg32Float,           // VK_FORMAT_R32G32_SFLOAT
        109 => TextureFormat.Rgba32Float,         // VK_FORMAT_R32G32B32A32_SFLOAT
        122 => TextureFormat.B10G11R11UFloat,     // VK_FORMAT_B10G11R11_UFLOAT_PACK32
        123 => TextureFormat.Rgb9E5UFloat,        // VK_FORMAT_E5B9G9R9_UFLOAT_PACK32
        131 => TextureFormat.Bc1RgbaUnorm,        // VK_FORMAT_BC1_RGB_UNORM_BLOCK
        132 => TextureFormat.Bc1RgbaUnormSrgb,    // VK_FORMAT_BC1_RGB_SRGB_BLOCK
        133 => TextureFormat.Bc1RgbaUnorm,        // VK_FORMAT_BC1_RGBA_UNORM_BLOCK
        134 => TextureFormat.Bc1RgbaUnormSrgb,    // VK_FORMAT_BC1_RGBA_SRGB_BLOCK
        135 => TextureFormat.Bc2Unorm,            // VK_FORMAT_BC2_UNORM_BLOCK
        136 => TextureFormat.Bc2UnormSrgb,        // VK_FORMAT_BC2_SRGB_BLOCK
        137 => TextureFormat.Bc3Unorm,            // VK_FORMAT_BC3_UNORM_BLOCK
        138 => TextureFormat.Bc3UnormSrgb,        // VK_FORMAT_BC3_SRGB_BLOCK
        139 => TextureFormat.Bc4Unorm,            // VK_FORMAT_BC4_UNORM_BLOCK
        140 => TextureFormat.Bc4Snorm,            // VK_FORMAT_BC4_SNORM_BLOCK
        141 => TextureFormat.Bc5Unorm,            // VK_FORMAT_BC5_UNORM_BLOCK
        142 => TextureFormat.Bc5Snorm,            // VK_FORMAT_BC5_SNORM_BLOCK
        143 => TextureFormat.Bc6HUFloat,          // VK_FORMAT_BC6H_UFLOAT_BLOCK
        144 => TextureFormat.Bc6HSFloat,          // VK_FORMAT_BC6H_SFLOAT_BLOCK
        145 => TextureFormat.Bc7Unorm,            // VK_FORMAT_BC7_UNORM_BLOCK
        146 => TextureFormat.Bc7UnormSrgb,        // VK_FORMAT_BC7_SRGB_BLOCK
        147 => TextureFormat.Etc2Rgb8Unorm,       // VK_FORMAT_ETC2_R8G8B8_UNORM_BLOCK
        148 => TextureFormat.Etc2Rgb8UnormSrgb,   // VK_FORMAT_ETC2_R8G8B8_SRGB_BLOCK
        149 => TextureFormat.Etc2Rgb8A1Unorm,     // VK_FORMAT_ETC2_R8G8B8A1_UNORM_BLOCK
        150 => TextureFormat.Etc2Rgb8A1UnormSrgb, // VK_FORMAT_ETC2_R8G8B8A1_SRGB_BLOCK
        151 => TextureFormat.Etc2Rgba8Unorm,      // VK_FORMAT_ETC2_R8G8B8A8_UNORM_BLOCK
        152 => TextureFormat.Etc2Rgba8UnormSrgb,  // VK_FORMAT_ETC2_R8G8B8A8_SRGB_BLOCK
        153 => TextureFormat.EacR11Unorm,         // VK_FORMAT_EAC_R11_UNORM_BLOCK
        154 => TextureFormat.EacR11Snorm,         // VK_FORMAT_EAC_R11_SNORM_BLOCK
        155 => TextureFormat.EacRg11Unorm,        // VK_FORMAT_EAC_R11G11_UNORM_BLOCK
        156 => TextureFormat.EacRg11Snorm,        // VK_FORMAT_EAC_R11G11_SNORM_BLOCK

        // VK_FORMAT_ASTC_*x*_{UNORM,SRGB}_BLOCK (157..184), unorm then srgb per footprint.
        >= 157 and <= 184 => AstcFormats.LdrFormat((int)(vkFormat - 157) / 2, ((vkFormat - 157) & 1) == 1),

        // VK_FORMAT_ASTC_*x*_SFLOAT_BLOCK: the same blocks, which decode to HDR when they hold it.
        >= 1000066000 and <= 1000066013 => AstcFormats.LdrFormat((int)(vkFormat - 1000066000), srgb: false),
        _ => TextureFormat.Unknown,
    };

    public static string VkName(uint vkFormat) => vkFormat switch
    {
        0 => "VK_FORMAT_UNDEFINED",
        2 => "VK_FORMAT_R4G4B4A4_UNORM_PACK16",
        4 => "VK_FORMAT_R5G6B5_UNORM_PACK16",
        6 => "VK_FORMAT_R5G5B5A1_UNORM_PACK16",
        8 => "VK_FORMAT_A1R5G5B5_UNORM_PACK16",
        9 => "VK_FORMAT_R8_UNORM",
        10 => "VK_FORMAT_R8_SNORM",
        13 => "VK_FORMAT_R8_UINT",
        14 => "VK_FORMAT_R8_SINT",
        15 => "VK_FORMAT_R8_SRGB",
        16 => "VK_FORMAT_R8G8_UNORM",
        23 => "VK_FORMAT_R8G8B8_UNORM",
        29 => "VK_FORMAT_R8G8B8_SRGB",
        30 => "VK_FORMAT_B8G8R8_UNORM",
        36 => "VK_FORMAT_B8G8R8_SRGB",
        64 => "VK_FORMAT_A2B10G10R10_UNORM_PACK32",
        70 => "VK_FORMAT_R16_UNORM",
        37 => "VK_FORMAT_R8G8B8A8_UNORM",
        38 => "VK_FORMAT_R8G8B8A8_SNORM",
        43 => "VK_FORMAT_R8G8B8A8_SRGB",
        44 => "VK_FORMAT_B8G8R8A8_UNORM",
        50 => "VK_FORMAT_B8G8R8A8_SRGB",
        76 => "VK_FORMAT_R16_SFLOAT",
        83 => "VK_FORMAT_R16G16_SFLOAT",
        90 => "VK_FORMAT_R16G16B16_SFLOAT",
        97 => "VK_FORMAT_R16G16B16A16_SFLOAT",
        100 => "VK_FORMAT_R32_SFLOAT",
        103 => "VK_FORMAT_R32G32_SFLOAT",
        109 => "VK_FORMAT_R32G32B32A32_SFLOAT",
        122 => "VK_FORMAT_B10G11R11_UFLOAT_PACK32",
        123 => "VK_FORMAT_E5B9G9R9_UFLOAT_PACK32",
        131 => "VK_FORMAT_BC1_RGB_UNORM_BLOCK",
        132 => "VK_FORMAT_BC1_RGB_SRGB_BLOCK",
        133 => "VK_FORMAT_BC1_RGBA_UNORM_BLOCK",
        134 => "VK_FORMAT_BC1_RGBA_SRGB_BLOCK",
        135 => "VK_FORMAT_BC2_UNORM_BLOCK",
        136 => "VK_FORMAT_BC2_SRGB_BLOCK",
        137 => "VK_FORMAT_BC3_UNORM_BLOCK",
        138 => "VK_FORMAT_BC3_SRGB_BLOCK",
        139 => "VK_FORMAT_BC4_UNORM_BLOCK",
        140 => "VK_FORMAT_BC4_SNORM_BLOCK",
        141 => "VK_FORMAT_BC5_UNORM_BLOCK",
        142 => "VK_FORMAT_BC5_SNORM_BLOCK",
        143 => "VK_FORMAT_BC6H_UFLOAT_BLOCK",
        144 => "VK_FORMAT_BC6H_SFLOAT_BLOCK",
        145 => "VK_FORMAT_BC7_UNORM_BLOCK",
        146 => "VK_FORMAT_BC7_SRGB_BLOCK",
        147 => "VK_FORMAT_ETC2_R8G8B8_UNORM_BLOCK",
        148 => "VK_FORMAT_ETC2_R8G8B8_SRGB_BLOCK",
        149 => "VK_FORMAT_ETC2_R8G8B8A1_UNORM_BLOCK",
        150 => "VK_FORMAT_ETC2_R8G8B8A1_SRGB_BLOCK",
        151 => "VK_FORMAT_ETC2_R8G8B8A8_UNORM_BLOCK",
        152 => "VK_FORMAT_ETC2_R8G8B8A8_SRGB_BLOCK",
        153 => "VK_FORMAT_EAC_R11_UNORM_BLOCK",
        154 => "VK_FORMAT_EAC_R11_SNORM_BLOCK",
        155 => "VK_FORMAT_EAC_R11G11_UNORM_BLOCK",
        156 => "VK_FORMAT_EAC_R11G11_SNORM_BLOCK",
        >= 157 and <= 184 => $"VK_FORMAT_ASTC_{AstcFormats.Footprints[(vkFormat - 157) / 2]}_{((vkFormat - 157) % 2 == 0 ? "UNORM" : "SRGB")}_BLOCK",
        >= 1000066000 and <= 1000066013 => $"VK_FORMAT_ASTC_{AstcFormats.Footprints[vkFormat - 1000066000]}_SFLOAT_BLOCK",
        _ => $"VkFormat {vkFormat}",
    };

    /// <summary>
    /// A clause naming the supported-decoder family a VkFormat belongs to, so the reader can say
    /// "Basis - not yet supported" instead of a bare number. Basis (VK_FORMAT_UNDEFINED) and HDR ASTC
    /// (the SFLOAT extension range) are what KTX2 still carries that we don't decode yet.
    /// </summary>
    public static string DescribeUnsupportedVk(uint vkFormat) => vkFormat switch
    {
        0 => "VK_FORMAT_UNDEFINED (Basis Universal supercompression — not yet supported)",
        _ => VkName(vkFormat),
    };
}
