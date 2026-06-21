using System.Buffers.Binary;

namespace Lyra.ManagedCodecs.Texture.Dds;

/// <summary>
/// Reads the structure of a DirectDraw Surface (.dds) file into a <see cref="TextureData"/>: it owns
/// the DXGI_FORMAT (DX10 header) and legacy FourCC / bitmask → <see cref="TextureFormat"/> mapping,
/// and validates every subresource byte range against the file length before exposing it. Parsing
/// allocates nothing beyond the subresource list; surface bytes stay as views into the source.
///
/// Treats input as hostile: dimensions, mip/array counts, and offsets are all bounds-checked, and
/// surface sizes go through the <c>checked</c> <see cref="TextureFormats.SurfaceByteSize"/>.
/// </summary>
public static class DdsReader
{
    private const uint Magic = 0x20534444; // "DDS "
    private const int HeaderSize = 124;
    private const int Dxt10HeaderSize = 20;
    private const int PixelFormatOffset = 4 + 72; // file start + offset of DDS_PIXELFORMAT in header

    private const uint DdsdMipMapCount = 0x20000;

    private const uint DdpfAlphaPixels = 0x1;
    private const uint DdpfFourCc = 0x4;
    private const uint DdpfRgb = 0x40;
    private const uint DdpfBumpDudv = 0x00080000; // signed (snorm) bump/normal data

    private const uint Caps2Cubemap = 0x200;
    private const uint Caps2Volume = 0x200000;

    private const uint MiscTextureCube = 0x4; // DDS_RESOURCE_MISC_TEXTURECUBE

    public static TextureData Read(ReadOnlyMemory<byte> file)
    {
        var span = file.Span;
        if (span.Length < 4 + HeaderSize)
        {
            throw new InvalidDataException("DDS: file is too small to contain a header.");
        }

        if (Read32(span, 0) != Magic)
        {
            throw new InvalidDataException("DDS: missing 'DDS ' magic.");
        }

        if (Read32(span, 4) != HeaderSize)
        {
            throw new InvalidDataException("DDS: unexpected header size.");
        }

        // File-absolute offsets: 4-byte magic, then DDS_HEADER. dwFlags@8, dwHeight@12, dwWidth@16,
        // dwPitchOrLinearSize@20, dwDepth@24, dwMipMapCount@28.
        var flags = Read32(span, 8);
        var height = (int)Read32(span, 12);
        var width = (int)Read32(span, 16);
        var depthField = (int)Read32(span, 24);
        var mipField = (int)Read32(span, 28);

        var pfFlags = Read32(span, PixelFormatOffset + 4);
        var fourCc = Read32(span, PixelFormatOffset + 8);
        var caps2 = Read32(span, 4 + 108);

        var hasDx10 = (pfFlags & DdpfFourCc) != 0 && fourCc == FourCc("DX10");
        var dataOffset = 4 + HeaderSize + (hasDx10 ? Dxt10HeaderSize : 0);
        if (span.Length < dataOffset)
        {
            throw new InvalidDataException("DDS: file is truncated before the pixel data.");
        }

        var isVolume = (caps2 & Caps2Volume) != 0;
        var isCubemap = (caps2 & Caps2Cubemap) != 0;
        var arraySize = 1;

        TextureFormat format;
        var dxgiFormat = 0u;
        {
            if (hasDx10)
            {
                dxgiFormat = Read32(span, 4 + HeaderSize + 0);
                var resourceDimension = Read32(span, 4 + HeaderSize + 4);
                var miscFlag = Read32(span, 4 + HeaderSize + 8);
                arraySize = (int)Read32(span, 4 + HeaderSize + 12);

                format = MapDxgi(dxgiFormat);
                isVolume = resourceDimension == 4; // D3D11_RESOURCE_DIMENSION_TEXTURE3D
                isCubemap = (miscFlag & MiscTextureCube) != 0;

                if (arraySize is < 1 or > 0xFFFF)
                {
                    throw new InvalidDataException($"DDS: implausible array size {arraySize}.");
                }
            }
            else
            {
                format = MapLegacy(pfFlags, fourCc, span);
            }
        }

        if (format == TextureFormat.Unknown)
        {
            throw new NotSupportedException($"DDS: {DescribeUnsupported(hasDx10, dxgiFormat, fourCc)}.");
        }

        if (width is <= 0 or > TextureLayout.MaxDimension || height is <= 0 or > TextureLayout.MaxDimension)
        {
            throw new InvalidDataException($"DDS: implausible dimensions {width}x{height}.");
        }

        var depth = isVolume ? Math.Max(1, depthField) : 1;
        if (depth > TextureLayout.MaxDimension)
        {
            throw new InvalidDataException($"DDS: implausible depth {depth}.");
        }

        var mipLevels = (flags & DdsdMipMapCount) != 0 || mipField > 0 ? mipField : 1;
        if (mipLevels < 1)
        {
            mipLevels = 1;
        }

        var maxMips = TextureLayout.MaxMipLevels(width, height, depth);
        if (mipLevels > maxMips)
        {
            throw new InvalidDataException($"DDS: mip count {mipLevels} exceeds the maximum {maxMips} for {width}x{height}x{depth}.");
        }

        var faces = isCubemap ? 6 : 1;
        var arrayCount = isVolume ? 1 : arraySize; // volumes cannot also be arrays/cubes
        var kind = TextureLayout.ResolveKind(isVolume, isCubemap, arrayCount);

        var subresources = EnumerateSubresources(file, format, width, height, depth, mipLevels, arrayCount, faces, isVolume, dataOffset);

        return new TextureData
        {
            Format = format,
            FormatName = ResolveFormatName(hasDx10, fourCc, pfFlags, format),
            Kind = kind,
            Width = width,
            Height = height,
            Depth = depth,
            MipLevels = mipLevels,
            ArrayLayers = arrayCount,
            Subresources = subresources,
        };
    }

    private static List<Subresource> EnumerateSubresources(
        ReadOnlyMemory<byte> file, TextureFormat format,
        int width, int height, int depth, int mipLevels, int arrayCount, int faces, bool isVolume, int dataOffset)
    {
        var subresources = new List<Subresource>(arrayCount * faces * mipLevels);
        long offset = dataOffset;
        var fileLength = file.Length;

        for (var layer = 0; layer < arrayCount; layer++)
        for (var face = 0; face < faces; face++)
        for (var mip = 0; mip < mipLevels; mip++)
        {
            var w = Math.Max(1, width >> mip);
            var h = Math.Max(1, height >> mip);
            var d = isVolume ? Math.Max(1, depth >> mip) : 1;

            // Volume mips stack d single-slice surfaces.
            var size = checked(TextureFormats.SurfaceByteSize(format, w, h) * d);

            if (offset + size > fileLength)
                throw new InvalidDataException($"DDS: subresource (layer {layer}, face {face}, mip {mip}) runs past the end of the file.");

            subresources.Add(new Subresource
            {
                MipLevel = mip,
                ArrayLayer = layer,
                Face = face,
                Width = w,
                Height = h,
                Depth = d,
                Data = file.Slice((int)offset, (int)size),
            });

            offset += size;
        }

        return subresources;
    }

    // ------------------------------------------------------------------
    //  Format mapping
    // ------------------------------------------------------------------

    private static TextureFormat MapDxgi(uint dxgiFormat) => dxgiFormat switch
    {
        28 => TextureFormat.Rgba8Unorm,       // R8G8B8A8_UNORM
        29 => TextureFormat.Rgba8UnormSrgb,   // R8G8B8A8_UNORM_SRGB
        87 => TextureFormat.Bgra8Unorm,       // B8G8R8A8_UNORM
        91 => TextureFormat.Bgra8UnormSrgb,   // B8G8R8A8_UNORM_SRGB
        71 => TextureFormat.Bc1RgbaUnorm,     // BC1_UNORM
        72 => TextureFormat.Bc1RgbaUnormSrgb, // BC1_UNORM_SRGB
        74 => TextureFormat.Bc2Unorm,         // BC2_UNORM
        75 => TextureFormat.Bc2UnormSrgb,     // BC2_UNORM_SRGB
        77 => TextureFormat.Bc3Unorm,         // BC3_UNORM
        78 => TextureFormat.Bc3UnormSrgb,     // BC3_UNORM_SRGB
        80 => TextureFormat.Bc4Unorm,         // BC4_UNORM
        81 => TextureFormat.Bc4Snorm,         // BC4_SNORM
        83 => TextureFormat.Bc5Unorm,         // BC5_UNORM
        84 => TextureFormat.Bc5Snorm,         // BC5_SNORM
        95 => TextureFormat.Bc6HUFloat,       // BC6H_UF16
        94 => TextureFormat.Bc6HUFloat,       // BC6H_TYPELESS -> treat as unsigned
        96 => TextureFormat.Bc6HSFloat,       // BC6H_SF16
        98 => TextureFormat.Bc7Unorm,         // BC7_UNORM
        99 => TextureFormat.Bc7UnormSrgb,     // BC7_UNORM_SRGB
        10 => TextureFormat.Rgba16Float,      // R16G16B16A16_FLOAT
        2 => TextureFormat.Rgba32Float,       // R32G32B32A32_FLOAT
        31 => TextureFormat.Rgba8Snorm,       // R8G8B8A8_SNORM
        61 => TextureFormat.R8Unorm,          // R8_UNORM
        49 => TextureFormat.Rg8Unorm,         // R8G8_UNORM
        >= 133 and <= 188 => MapDxgiAstc(dxgiFormat),
        _ => TextureFormat.Unknown,
    };

    /// <summary>
    /// Maps the DXGI ASTC formats (133..188): four slots per footprint - TYPELESS, UNORM, UNORM_SRGB,
    /// reserved - in the canonical footprint order. TYPELESS is treated as UNORM (as with BC6H here).
    /// </summary>
    private static TextureFormat MapDxgiAstc(uint dxgiFormat)
    {
        var offset = (int)dxgiFormat - 133;
        var footprint = offset / 4;
        var slot = offset % 4;
        if (footprint >= AstcFormats.Count || slot == 3)
        {
            return TextureFormat.Unknown; // reserved slot / out of range
        }

        return AstcFormats.LdrFormat(footprint, srgb: slot == 2); // 0 TYPELESS, 1 UNORM -> unorm; 2 -> srgb
    }

    private static TextureFormat MapLegacy(uint pfFlags, uint fourCc, ReadOnlySpan<byte> span)
    {
        if ((pfFlags & DdpfFourCc) != 0)
        {
            return fourCc switch
            {
                // Some D3D9 exporters store a numeric D3DFORMAT in the FourCC field instead of 4 chars.
                113 => TextureFormat.Rgba16Float, // D3DFMT_A16B16G16R16F
                116 => TextureFormat.Rgba32Float, // D3DFMT_A32B32G32R32F
                _ when fourCc == FourCc("DXT1") => TextureFormat.Bc1RgbaUnorm,
                _ when fourCc == FourCc("DXT2") || fourCc == FourCc("DXT3") => TextureFormat.Bc2Unorm,
                _ when fourCc == FourCc("DXT4") || fourCc == FourCc("DXT5") => TextureFormat.Bc3Unorm,
                _ when fourCc == FourCc("ATI1") || fourCc == FourCc("BC4U") => TextureFormat.Bc4Unorm,
                _ when fourCc == FourCc("BC4S") => TextureFormat.Bc4Snorm,
                _ when fourCc == FourCc("ATI2") || fourCc == FourCc("BC5U") => TextureFormat.Bc5Unorm,
                _ when fourCc == FourCc("BC5S") => TextureFormat.Bc5Snorm,
                _ => TextureFormat.Unknown,
            };
        }

        if ((pfFlags & DdpfRgb) != 0)
        {
            var bitCount = Read32(span, PixelFormatOffset + 12);
            var rMask = Read32(span, PixelFormatOffset + 16);
            var gMask = Read32(span, PixelFormatOffset + 20);
            var bMask = Read32(span, PixelFormatOffset + 24);
            var aMask = (pfFlags & DdpfAlphaPixels) != 0 ? Read32(span, PixelFormatOffset + 28) : 0u;

            if (bitCount == 32 && gMask == 0x0000FF00)
            {
                // The two ubiquitous 32-bit layouts; treated as linear (legacy DDS has no sRGB flag).
                if (rMask == 0x00FF0000 && bMask == 0x000000FF && aMask == 0xFF000000)
                    return TextureFormat.Bgra8Unorm;
                
                if (rMask == 0x000000FF && bMask == 0x00FF0000 && aMask == 0xFF000000)
                    return TextureFormat.Rgba8Unorm;
            }
        }

        // Signed (snorm) bump/normal maps, e.g. D3DFMT_Q8W8V8U8: 32bpp with RGBA-order masks.
        if ((pfFlags & DdpfBumpDudv) != 0)
        {
            var bitCount = Read32(span, PixelFormatOffset + 12);
            var rMask = Read32(span, PixelFormatOffset + 16);
            var gMask = Read32(span, PixelFormatOffset + 20);
            var bMask = Read32(span, PixelFormatOffset + 24);
            var aMask = Read32(span, PixelFormatOffset + 28);

            if (bitCount == 32 && rMask == 0x000000FF && gMask == 0x0000FF00 && bMask == 0x00FF0000 && aMask == 0xFF000000)
                return TextureFormat.Rgba8Snorm;
        }

        return TextureFormat.Unknown;
    }

    // ------------------------------------------------------------------
    //  Helpers
    // ------------------------------------------------------------------

    private static string DescribeUnsupported(bool hasDx10, uint dxgiFormat, uint fourCc)
    {
        if (hasDx10)
        {
            // DXGI ASTC occupies 133..191; LDR UNORM/SRGB are decoded, so reaching here for that range
            // means a TYPELESS-only or reserved slot we don't surface.
            var astc = dxgiFormat is >= 133 and <= 191 ? " (unsupported ASTC slot)" : "";
            return $"unsupported DXGI_FORMAT {dxgiFormat}{astc}";
        }

        if ((fourCc >> 8) == 0)
            return $"unsupported legacy D3DFORMAT {fourCc}";

        return $"unsupported FourCC '{FourCcString(fourCc)}'";
    }

    /// <summary>
    /// A human-readable label for the format as it was stored. Legacy four-character codes keep their
    /// literal spelling (e.g. "DXT4", "ATI2"); everything else gets its canonical DXGI-style name.
    /// </summary>
    private static string ResolveFormatName(bool hasDx10, uint fourCc, uint pfFlags, TextureFormat format)
    {
        if (!hasDx10 && (pfFlags & DdpfFourCc) != 0)
        {
            // A real four-character code is shown literally (DXT4, ATI2, …). A numeric D3DFORMAT keeps
            // its canonical name but appends the raw FourCC char, so it can be cross-referenced with
            // tools that only print the undecoded code (e.g. "FourCC: q").
            return (fourCc >> 8) != 0
                ? FourCcString(fourCc)
                : $"{CanonicalName(format)} (FourCC '{FourCcString(fourCc)}')";
        }

        return CanonicalName(format);
    }

    private static string CanonicalName(TextureFormat format) => format switch
    {
        TextureFormat.Rgba8Unorm => "R8G8B8A8_UNORM",
        TextureFormat.Rgba8UnormSrgb => "R8G8B8A8_UNORM_SRGB",
        TextureFormat.Rgba8Snorm => "R8G8B8A8_SNORM",
        TextureFormat.Bgra8Unorm => "B8G8R8A8_UNORM",
        TextureFormat.Bgra8UnormSrgb => "B8G8R8A8_UNORM_SRGB",
        TextureFormat.Rgba16Float => "R16G16B16A16_FLOAT",
        TextureFormat.Rgba32Float => "R32G32B32A32_FLOAT",
        TextureFormat.Bc1RgbaUnorm => "BC1_UNORM",
        TextureFormat.Bc1RgbaUnormSrgb => "BC1_UNORM_SRGB",
        TextureFormat.Bc2Unorm => "BC2_UNORM",
        TextureFormat.Bc2UnormSrgb => "BC2_UNORM_SRGB",
        TextureFormat.Bc3Unorm => "BC3_UNORM",
        TextureFormat.Bc3UnormSrgb => "BC3_UNORM_SRGB",
        TextureFormat.Bc4Unorm => "BC4_UNORM",
        TextureFormat.Bc4Snorm => "BC4_SNORM",
        TextureFormat.Bc5Unorm => "BC5_UNORM",
        TextureFormat.Bc5Snorm => "BC5_SNORM",
        TextureFormat.Bc7Unorm => "BC7_UNORM",
        TextureFormat.Bc7UnormSrgb => "BC7_UNORM_SRGB",
        TextureFormat.Bc6HUFloat => "BC6H_UF16",
        TextureFormat.Bc6HSFloat => "BC6H_SF16",
        _ => format.ToString(),
    };

    private static string FourCcString(uint fourCc)
    {
        Span<char> chars =
        [
            (char)(fourCc & 0xFF), (char)((fourCc >> 8) & 0xFF),
            (char)((fourCc >> 16) & 0xFF), (char)((fourCc >> 24) & 0xFF),
        ];
        return new string(chars).TrimEnd('\0');
    }

    private static uint FourCc(string code)
        => (uint)(code[0] | (code[1] << 8) | (code[2] << 16) | (code[3] << 24));

    private static uint Read32(ReadOnlySpan<byte> span, int offset)
        => BinaryPrimitives.ReadUInt32LittleEndian(span.Slice(offset, 4));
}