using System.Runtime.InteropServices;
using Lyra.ManagedCodecs.Texture.Blocks;

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
    }

    // The uncompressed float paths reinterpret the source bytes directly, so they assume a
    // little-endian host (true for every platform .NET targets; DDS is little-endian).
    private static void DecodeRgba16Float(ReadOnlySpan<byte> src, Span<float> dst, int pixels)
    {
        var halves = MemoryMarshal.Cast<byte, Half>(src);
        var count = pixels * 4;
        for (var i = 0; i < count; i++)
        {
            dst[i] = (float)halves[i];
        }
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

            dst[i] = (byte)((((v + 127) * 255) + 127) / 254);
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
        {
            for (var bx = 0; bx < blocksX; bx++)
            {
                var block = src.Slice(srcOffset, bytesPerBlock);
                srcOffset += bytesPerBlock;

                DecodeBlock(format, block, blockRgba);

                // Copy the (possibly partial) 4x4 block into the destination, clipping at the edges.
                var pxX = bx * 4;
                var pxY = by * 4;
                var copyW = Math.Min(4, width - pxX);
                var copyH = Math.Min(4, height - pxY);

                for (var ry = 0; ry < copyH; ry++)
                {
                    var srcRow = blockRgba.Slice(ry * 4 * 4, copyW * 4);
                    var dstIndex = ((pxY + ry) * width + pxX) * 4;
                    srcRow.CopyTo(dst.Slice(dstIndex, copyW * 4));
                }
            }
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
            default:
                throw new NotSupportedException($"Texture: no block decoder for {format}.");
        }
    }
}
