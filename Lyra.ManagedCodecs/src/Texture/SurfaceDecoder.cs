using System.Runtime.InteropServices;
using Lyra.ManagedCodecs.Texture.Blocks;
using Lyra.ManagedCodecs.Texture.Blocks.Astc;

namespace Lyra.ManagedCodecs.Texture;

/// <summary>
/// Decodes one surface (a single mip/face/layer) from its stored bytes into 8-bit RGBA, top-left
/// origin, laid out as <see cref="TextureFormats.DecodedFormat"/>. Zero-allocation: decodes straight
/// into the caller's destination. Faithful — no color transform; an sRGB source decodes to
/// sRGB-tagged bytes and the display path linearises.
/// </summary>
public static class SurfaceDecoder
{
    public static void DecodeSurface(TextureFormat format, ReadOnlySpan<byte> src, Span<byte> dst, int width, int height)
    {
        var dstRequired = ValidateSurface(format, src.Length, dst.Length, width, height, dstUnit: "bytes");

        if (TextureFormats.Info(format).IsAstc)
        {
            DecodeAstcSurface(format, src, dst, width, height);
            return;
        }

        switch (format)
        {
            case TextureFormat.Rgba8Unorm:
            case TextureFormat.Rgba8UnormSrgb:
                src[..(int)dstRequired].CopyTo(dst);
                return;

            case TextureFormat.Bgra8Unorm:
            case TextureFormat.Bgra8UnormSrgb:
                SwizzleBgraToRgba(src, dst, width * height);
                return;

            case TextureFormat.Rgba8Snorm:
                DecodeRgba8Snorm(src, dst, width * height);
                return;

            case TextureFormat.R8Unorm:
            case TextureFormat.R8Uint: // integer values are shown directly as grayscale
                ExpandChannels(src, dst, width * height, sourceChannels: 1);
                return;

            case TextureFormat.Rg8Unorm:
                ExpandChannels(src, dst, width * height, sourceChannels: 2);
                return;

            case TextureFormat.Rgb8Unorm:
            case TextureFormat.Rgb8UnormSrgb:
                ExpandChannels(src, dst, width * height, sourceChannels: 3);
                return;

            case TextureFormat.R8Snorm:
            case TextureFormat.R8Sint: // signed integers reuse the snorm grayscale remap for display
                DecodeR8Snorm(src, dst, width * height);
                return;

            case TextureFormat.R16Unorm:
                DecodeR16(src, dst, width * height);
                return;

            case TextureFormat.Rgba4Unorm:
                DecodePacked16(src, dst, width * height, PackedLayout.Rgba4);
                return;

            case TextureFormat.Rgb5A1Unorm:
                DecodePacked16(src, dst, width * height, PackedLayout.Rgb5A1);
                return;

            case TextureFormat.Rgb565Unorm:
                DecodePacked16(src, dst, width * height, PackedLayout.Rgb565);
                return;

            case TextureFormat.Rgb10A2Unorm:
                DecodeRgb10A2(src, dst, width * height);
                return;

            default:
                DecodeBlocks(format, src, dst, width, height);
                return;
        }
    }

    /// <summary>
    /// Decodes an HDR surface (BC6H, or uncompressed RGBA16F/RGBA32F) into linear RGBA float, top-left
    /// origin. Separate from the 8-bit path because the data is scene-referred and must not be
    /// quantized before tone-mapping.
    /// </summary>
    public static void DecodeSurfaceHdr(TextureFormat format, ReadOnlySpan<byte> src, Span<float> dst, int width, int height)
    {
        var dstRequired = ValidateSurface(format, src.Length, dst.Length, width, height, dstUnit: "floats");

        switch (format)
        {
            case TextureFormat.Bc6HUFloat:
                DecodeBc6hSurface(src, dst, width, height, signed: false);
                return;
            case TextureFormat.Bc6HSFloat:
                DecodeBc6hSurface(src, dst, width, height, signed: true);
                return;
            case TextureFormat.Rgba16Float:
                DecodeRgba16Float(src, dst, width * height);
                return;
            case TextureFormat.Rgba32Float:
                MemoryMarshal.Cast<byte, float>(src)[..(int)dstRequired].CopyTo(dst);
                return;
            case TextureFormat.R16Float:
                DecodeR16FloatGray(src, dst, width * height);
                return;
            case TextureFormat.R32Float:
                DecodeR32FloatGray(src, dst, width * height);
                return;
            case TextureFormat.Rgb16Float:
                DecodeRgb16Float(src, dst, width * height);
                return;
            case TextureFormat.B10G11R11UFloat:
                DecodeB10G11R11(src, dst, width * height);
                return;
            case TextureFormat.Rgb9E5UFloat:
                DecodeRgb9E5(src, dst, width * height);
                return;
            default:
                throw new NotSupportedException($"Texture: {format} is not an HDR surface format.");
        }
    }

    /// <summary>
    /// Validates a decode request and returns the required RGBA destination element count
    /// (<c>width * height * 4</c>). The destination is always RGBA; <paramref name="dstUnit"/> only
    /// labels its element size in error messages ("bytes" for 8-bit, "floats" for HDR).
    /// </summary>
    private static long ValidateSurface(TextureFormat format, int srcLength, int dstLength, int width, int height, string dstUnit)
    {
        if (width <= 0 || height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(width), $"Texture: invalid surface size {width}x{height}.");
        }

        var required = TextureFormats.SurfaceByteSize(format, width, height);
        if (srcLength < required)
        {
            throw new ArgumentException($"Texture: source surface is {srcLength} bytes, need {required} for {width}x{height} {format}.", nameof(srcLength));
        }

        var dstRequired = checked((long)width * height * 4);
        if (dstLength < dstRequired)
        {
            throw new ArgumentException($"Texture: destination is {dstLength} {dstUnit}, need {dstRequired} for {width}x{height} RGBA.", nameof(dstLength));
        }

        return dstRequired;
    }

    private static void DecodeBc6hSurface(ReadOnlySpan<byte> src, Span<float> dst, int width, int height, bool signed)
    {
        var blocksX = (width + 3) / 4;
        var blocksY = (height + 3) / 4;
        Span<float> blockRgba = stackalloc float[64]; // 4x4 * RGBA
        var srcOffset = 0;

        for (var by = 0; by < blocksY; by++)
        for (var bx = 0; bx < blocksX; bx++)
        {
            Bc6hBlockDecoder.DecodeBc6h(src.Slice(srcOffset, 16), blockRgba, signed);
            srcOffset += 16;

            EmitBlock(blockRgba, dst, bx, by, width, height);
        }
    }

    /// <summary>
    /// Decodes an ASTC surface, tiling over its (variable) footprint. Each 16-byte block is decoded
    /// into a temporary buffer and copied into the destination, clipping at edges where the surface
    /// isn't a whole number of blocks. A block that fails to decode (e.g. an HDR or malformed
    /// encoding) is filled with the conventional magenta error color rather than aborting the surface.
    /// </summary>
    private static void DecodeAstcSurface(TextureFormat format, ReadOnlySpan<byte> src, Span<byte> dst, int width, int height)
    {
        var info = TextureFormats.Info(format);
        var bw = info.BlockWidth;
        var bh = info.BlockHeight;
        var bd = info.IsAstc3D ? info.BlockDepth : 1;
        var blocksX = (width + bw - 1) / bw;
        var blocksY = (height + bh - 1) / bh;
        
        Span<byte> blockRgba = stackalloc byte[6 * 6 * 6 * 4]; // largest footprint (3D 6x6x6)
        var srcOffset = 0;

        for (var by = 0; by < blocksY; by++)
        for (var bx = 0; bx < blocksX; bx++)
        {
            if (!AstcBlockDecoder.TryDecode(src.Slice(srcOffset, 16), bw, bh, bd, blockRgba)) 
                FillError(blockRgba, bw * bh);

            srcOffset += 16;

            var pxX = bx * bw;
            var pxY = by * bh;
            var copyW = Math.Min(bw, width - pxX);
            var copyH = Math.Min(bh, height - pxY);
            for (var ry = 0; ry < copyH; ry++)
            {
                var srcRow = blockRgba.Slice(ry * bw * 4, copyW * 4);
                var dstIndex = ((pxY + ry) * width + pxX) * 4;
                srcRow.CopyTo(dst.Slice(dstIndex, copyW * 4));
            }
        }
    }

    private static void FillError(Span<byte> block, int texels)
    {
        for (var i = 0; i < texels; i++)
        {
            block[(i * 4) + 0] = 255;
            block[(i * 4) + 1] = 0;
            block[(i * 4) + 2] = 255;
            block[(i * 4) + 3] = 255;
        }
    }

    // The uncompressed float paths reinterpret the source bytes directly, so they assume a
    // little-endian host (true for every platform .NET targets; DDS is little-endian).
    private static void DecodeRgba16Float(ReadOnlySpan<byte> src, Span<float> dst, int pixels)
    {
        var halves = MemoryMarshal.Cast<byte, Half>(src);
        var count = pixels * 4;
        for (var i = 0; i < count; i++) 
            dst[i] = (float)halves[i];
    }

    /// <summary>
    /// Expands an uncompressed format with fewer than four 8-bit channels into RGBA. A single channel
    /// (R8) is shown as grayscale and two channels (RG8) as red+green with zero blue - matching the
    /// BC4/BC5/EAC conventions; three channels (RGB8) pass through with opaque alpha.
    /// </summary>
    private static void ExpandChannels(ReadOnlySpan<byte> src, Span<byte> dst, int pixels, int sourceChannels)
    {
        for (var i = 0; i < pixels; i++)
        {
            var s = i * sourceChannels;
            var d = i * 4;
            switch (sourceChannels)
            {
                case 1:
                    dst[d + 0] = dst[d + 1] = dst[d + 2] = src[s];
                    break;
                case 2:
                    dst[d + 0] = src[s];
                    dst[d + 1] = src[s + 1];
                    dst[d + 2] = 0;
                    break;
                default:
                    dst[d + 0] = src[s];
                    dst[d + 1] = src[s + 1];
                    dst[d + 2] = src[s + 2];
                    break;
            }

            dst[d + 3] = 255;
        }
    }

    private enum PackedLayout
    {
        Rgba4,
        Rgb5A1,
        Rgb565
    }

    /// <summary>Decodes a 16-bit-per-pixel packed integer format (little-endian) into RGBA8.</summary>
    private static void DecodePacked16(ReadOnlySpan<byte> src, Span<byte> dst, int pixels, PackedLayout layout)
    {
        for (var i = 0; i < pixels; i++)
        {
            var v = src[i * 2] | (src[(i * 2) + 1] << 8);
            var d = i * 4;
            switch (layout)
            {
                case PackedLayout.Rgba4:    // R4G4B4A4, R in the high nibble
                    dst[d + 0] = (byte)(((v >> 12) & 0xF) * 0x11);
                    dst[d + 1] = (byte)(((v >> 8) & 0xF) * 0x11);
                    dst[d + 2] = (byte)(((v >> 4) & 0xF) * 0x11);
                    dst[d + 3] = (byte)((v & 0xF) * 0x11);
                    break;
                case PackedLayout.Rgb5A1:   // R5G5B5A1
                    dst[d + 0] = Expand5((v >> 11) & 0x1F);
                    dst[d + 1] = Expand5((v >> 6) & 0x1F);
                    dst[d + 2] = Expand5((v >> 1) & 0x1F);
                    dst[d + 3] = (byte)((v & 1) * 255);
                    break;
                default:                    // Rgb565: R5G6B5
                    dst[d + 0] = Expand5((v >> 11) & 0x1F);
                    dst[d + 1] = (byte)(((v >> 5) & 0x3F) << 2 | ((v >> 5) & 0x3F) >> 4);
                    dst[d + 2] = Expand5(v & 0x1F);
                    dst[d + 3] = 255;
                    break;
            }
        }
    }

    /// <summary>Decodes A2B10G10R10 (32-bit packed, R in the low bits) into RGBA8.</summary>
    private static void DecodeRgb10A2(ReadOnlySpan<byte> src, Span<byte> dst, int pixels)
    {
        for (var i = 0; i < pixels; i++)
        {
            var s = i * 4;
            var v = (uint)(src[s] | (src[s + 1] << 8) | (src[s + 2] << 16) | (src[s + 3] << 24));
            var d = i * 4;
            dst[d + 0] = (byte)((v & 0x3FF) >> 2);
            dst[d + 1] = (byte)(((v >> 10) & 0x3FF) >> 2);
            dst[d + 2] = (byte)(((v >> 20) & 0x3FF) >> 2);
            dst[d + 3] = (byte)(((v >> 30) & 0x3) * 85); // 2-bit alpha -> 0/85/170/255
        }
    }

    /// <summary>Decodes R16 unorm as grayscale, taking the high byte for 8-bit display.</summary>
    private static void DecodeR16(ReadOnlySpan<byte> src, Span<byte> dst, int pixels)
    {
        for (var i = 0; i < pixels; i++)
        {
            var v = (byte)src[(i * 2) + 1]; // high byte of the little-endian 16-bit value
            var d = i * 4;
            dst[d + 0] = dst[d + 1] = dst[d + 2] = v;
            dst[d + 3] = 255;
        }
    }

    /// <summary>Decodes R8 snorm as grayscale, remapping the signed channel to [0,255].</summary>
    private static void DecodeR8Snorm(ReadOnlySpan<byte> src, Span<byte> dst, int pixels)
    {
        for (var i = 0; i < pixels; i++)
        {
            var v = (sbyte)src[i];
            if (v < -127)
                v = -127;

            var g = (byte)((((v + 127) * 255) + 127) / 254);
            var d = i * 4;
            dst[d + 0] = dst[d + 1] = dst[d + 2] = g;
            dst[d + 3] = 255;
        }
    }

    private static byte Expand5(int v) => (byte)((v << 3) | (v >> 2));

    private static void DecodeR16FloatGray(ReadOnlySpan<byte> src, Span<float> dst, int pixels)
    {
        var halves = MemoryMarshal.Cast<byte, Half>(src);
        for (var i = 0; i < pixels; i++)
        {
            var v = (float)halves[i];
            WriteGray(dst, i, v);
        }
    }

    private static void DecodeR32FloatGray(ReadOnlySpan<byte> src, Span<float> dst, int pixels)
    {
        var floats = MemoryMarshal.Cast<byte, float>(src);
        for (var i = 0; i < pixels; i++) 
            WriteGray(dst, i, floats[i]);
    }

    private static void DecodeRgb16Float(ReadOnlySpan<byte> src, Span<float> dst, int pixels)
    {
        var halves = MemoryMarshal.Cast<byte, Half>(src);
        for (var i = 0; i < pixels; i++)
        {
            var s = i * 3;
            var d = i * 4;
            dst[d + 0] = (float)halves[s + 0];
            dst[d + 1] = (float)halves[s + 1];
            dst[d + 2] = (float)halves[s + 2];
            dst[d + 3] = 1f;
        }
    }

    /// <summary>B10G11R11_UFLOAT (packed32): 11-bit R/G + 10-bit B unsigned floats, R in the low bits.</summary>
    private static void DecodeB10G11R11(ReadOnlySpan<byte> src, Span<float> dst, int pixels)
    {
        for (var i = 0; i < pixels; i++)
        {
            var s = i * 4;
            var v = (uint)(src[s] | (src[s + 1] << 8) | (src[s + 2] << 16) | (src[s + 3] << 24));
            var d = i * 4;
            dst[d + 0] = UnpackUFloat(v & 0x7FF, 6); // R: 11-bit (6 mantissa)
            dst[d + 1] = UnpackUFloat((v >> 11) & 0x7FF, 6); // G: 11-bit
            dst[d + 2] = UnpackUFloat((v >> 22) & 0x3FF, 5); // B: 10-bit (5 mantissa)
            dst[d + 3] = 1f;
        }
    }

    /// <summary>RGB9E5 (E5B9G9R9_UFLOAT): three 9-bit mantissas sharing a 5-bit exponent.</summary>
    private static void DecodeRgb9E5(ReadOnlySpan<byte> src, Span<float> dst, int pixels)
    {
        for (var i = 0; i < pixels; i++)
        {
            var s = i * 4;
            var v = (uint)(src[s] | (src[s + 1] << 8) | (src[s + 2] << 16) | (src[s + 3] << 24));
            var exp = (int)(v >> 27) & 0x1F;
            var scale = (float)Math.Pow(2.0, exp - 24); // bias 15 + 9 mantissa bits
            var d = i * 4;
            dst[d + 0] = (v & 0x1FF) * scale;
            dst[d + 1] = ((v >> 9) & 0x1FF) * scale;
            dst[d + 2] = ((v >> 18) & 0x1FF) * scale;
            dst[d + 3] = 1f;
        }
    }

    private static void WriteGray(Span<float> dst, int i, float v)
    {
        var d = i * 4;
        dst[d + 0] = dst[d + 1] = dst[d + 2] = v;
        dst[d + 3] = 1f;
    }

    /// <summary>Decodes one unsigned float with a 5-bit exponent (bias 15) and the given mantissa width.</summary>
    private static float UnpackUFloat(uint bits, int mantissaBits)
    {
        var e = (int)(bits >> mantissaBits) & 0x1F;
        var m = (int)(bits & ((1u << mantissaBits) - 1));
        var scale = 1f / (1 << mantissaBits);

        if (e == 0)
            return m == 0 ? 0f : (float)(m * scale * Math.Pow(2.0, -14)); // subnormal

        if (e == 31)
            return 65504f; // inf/NaN -> clamp to a large finite value for display

        return (float)((1.0 + (m * scale)) * Math.Pow(2.0, e - 15));
    }

    private static void SwizzleBgraToRgba(ReadOnlySpan<byte> src, Span<byte> dst, int pixels)
    {
        for (var i = 0; i < pixels; i++)
        {
            var s = i * 4;
            dst[s + 0] = src[s + 2]; // R <- B
            dst[s + 1] = src[s + 1]; // G
            dst[s + 2] = src[s + 0]; // B <- R
            dst[s + 3] = src[s + 3]; // A
        }
    }

    /// <summary>
    /// Decodes signed-normalized RGBA8 (e.g. D3DFMT_Q8W8V8U8 bump maps) for display: each signed
    /// channel in [-1,1] is remapped to [0,255], the standard normal-map visualization.
    /// </summary>
    private static void DecodeRgba8Snorm(ReadOnlySpan<byte> src, Span<byte> dst, int pixels)
    {
        var count = pixels * 4;
        for (var i = 0; i < count; i++)
        {
            var v = (sbyte)src[i];
            if (v < -127)
            {
                v = -127; // -128 is reserved; clamp to the symmetric range
            }

            dst[i] = (byte)(((v + 127) * 255 + 127) / 254);
        }
    }

    private static void DecodeBlocks(TextureFormat format, ReadOnlySpan<byte> src, Span<byte> dst, int width, int height)
    {
        var info = TextureFormats.Info(format);
        var bytesPerBlock = info.BytesPerBlock;
        var blocksX = (width + 3) / 4;
        var blocksY = (height + 3) / 4;

        Span<byte> blockRgba = stackalloc byte[64]; // 4x4 * RGBA
        var srcOffset = 0;

        for (var by = 0; by < blocksY; by++)
        for (var bx = 0; bx < blocksX; bx++)
        {
            var block = src.Slice(srcOffset, bytesPerBlock);
            srcOffset += bytesPerBlock;

            DecodeBlock(format, block, blockRgba);
            EmitBlock(blockRgba, dst, bx, by, width, height);
        }
    }

    /// <summary>
    /// Copies a decoded 4x4 RGBA block (16 texels, row-major) into the destination surface at block
    /// position (<paramref name="bx"/>, <paramref name="by"/>), clipping the rows and columns that fall
    /// outside a surface whose dimensions aren't a multiple of four. Generic over the texel element so
    /// the 8-bit and HDR-float paths share one implementation.
    /// </summary>
    private static void EmitBlock<T>(ReadOnlySpan<T> blockRgba, Span<T> dst, int bx, int by, int width, int height)
    {
        var pxX = bx * 4;
        var pxY = by * 4;
        var copyW = Math.Min(4, width - pxX);
        var copyH = Math.Min(4, height - pxY);

        for (var ry = 0; ry < copyH; ry++)
        {
            var srcRow = blockRgba.Slice(ry * 4 * 4, copyW * 4);
            var dstIndex = (((pxY + ry) * width) + pxX) * 4;
            srcRow.CopyTo(dst.Slice(dstIndex, copyW * 4));
        }
    }

    private static void DecodeBlock(TextureFormat format, ReadOnlySpan<byte> block, Span<byte> dst)
    {
        switch (format)
        {
            case TextureFormat.Bc1RgbaUnorm:
            case TextureFormat.Bc1RgbaUnormSrgb:
                BcBlockDecoder.DecodeBc1(block, dst);
                break;
            case TextureFormat.Bc2Unorm:
            case TextureFormat.Bc2UnormSrgb:
                BcBlockDecoder.DecodeBc2(block, dst);
                break;
            case TextureFormat.Bc3Unorm:
            case TextureFormat.Bc3UnormSrgb:
                BcBlockDecoder.DecodeBc3(block, dst);
                break;
            case TextureFormat.Bc4Unorm:
                BcBlockDecoder.DecodeBc4(block, dst, signed: false);
                break;
            case TextureFormat.Bc4Snorm:
                BcBlockDecoder.DecodeBc4(block, dst, signed: true);
                break;
            case TextureFormat.Bc5Unorm:
                BcBlockDecoder.DecodeBc5(block, dst, signed: false);
                break;
            case TextureFormat.Bc5Snorm:
                BcBlockDecoder.DecodeBc5(block, dst, signed: true);
                break;
            case TextureFormat.Bc7Unorm:
            case TextureFormat.Bc7UnormSrgb:
                Bc7BlockDecoder.DecodeBc7(block, dst);
                break;
            case TextureFormat.Etc2Rgb8Unorm:
            case TextureFormat.Etc2Rgb8UnormSrgb:
                Etc2BlockDecoder.DecodeRgb(block, dst, punchThrough: false);
                break;
            case TextureFormat.Etc2Rgb8A1Unorm:
            case TextureFormat.Etc2Rgb8A1UnormSrgb:
                Etc2BlockDecoder.DecodeRgb(block, dst, punchThrough: true);
                break;
            case TextureFormat.Etc2Rgba8Unorm:
            case TextureFormat.Etc2Rgba8UnormSrgb:
                Etc2BlockDecoder.DecodeRgba8(block, dst);
                break;
            case TextureFormat.EacR11Unorm:
                Etc2BlockDecoder.DecodeEacR11(block, dst, signed: false);
                break;
            case TextureFormat.EacR11Snorm:
                Etc2BlockDecoder.DecodeEacR11(block, dst, signed: true);
                break;
            case TextureFormat.EacRg11Unorm:
                Etc2BlockDecoder.DecodeEacRg11(block, dst, signed: false);
                break;
            case TextureFormat.EacRg11Snorm:
                Etc2BlockDecoder.DecodeEacRg11(block, dst, signed: true);
                break;
            default:
                throw new NotSupportedException($"Texture: no block decoder for {format}.");
        }
    }
}