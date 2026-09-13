using System.Runtime.InteropServices;
using Lyra.Imaging.Decoding.Support;
using SkiaSharp;
using Xunit;

namespace Lyra.Imaging.Tests.Decoding;

/// <summary>
/// Straight-alpha RGBA8 into the premultiplied form Skia draws.
/// </summary>
public class PremultiplyCopyTests
{
    [Fact]
    public void TransparentPixelsAreZeroedEntirely()
    {
        // Colour behind zero alpha must not survive: premultiplied, it would tint the edges.
        var pixels = Copy(1, 1, sourceStride: 4, [200, 100, 50, 0], out _);

        Assert.Equal([0, 0, 0, 0], pixels);
    }

    [Fact]
    public void OpaquePixelsAreCopiedVerbatim()
    {
        var pixels = Copy(1, 1, sourceStride: 4, [200, 100, 50, 255], out _);

        Assert.Equal([200, 100, 50, 255], pixels);
    }

    [Fact]
    public void PartialAlphaRoundsToNearest()
    {
        // (200 * 128 + 127) / 255 = 100, (100 * 128 + 127) / 255 = 50, (50 * 128 + 127) / 255 = 25.
        // Truncating without the +127 would give 100, 50, 25 one lower in the general case.
        var pixels = Copy(1, 1, sourceStride: 4, [200, 100, 50, 128], out _);

        Assert.Equal([100, 50, 25, 128], pixels);
    }

    [Fact]
    public void ARowsOwnStrideIsHonoured()
    {
        byte[] source =
        [
            10, 10, 10, 255, 20, 20, 20, 255, 0xFF, 0xFF, 0xFF, 0xFF, // row 0 + 4 bytes of padding
            30, 30, 30, 255, 40, 40, 40, 255, 0xFF, 0xFF, 0xFF, 0xFF  // row 1 + 4 bytes of padding
        ];

        var pixels = Copy(2, 2, sourceStride: 12, source, out _);

        Assert.Equal([10, 10, 10, 255, 20, 20, 20, 255], pixels[..8]);
        Assert.Equal([30, 30, 30, 255, 40, 40, 40, 255], pixels[8..16]);
    }

    [Fact]
    public void GreyIsReportedWhenEveryChannelAgrees()
    {
        Copy(1, 1, sourceStride: 4, [64, 64, 64, 255], out var opaqueGrey);
        Assert.True(opaqueGrey);

        Copy(1, 1, sourceStride: 4, [64, 64, 65, 255], out var opaqueColour);
        Assert.False(opaqueColour);
    }

    [Fact]
    public void GreyIsMeasuredOnThePremultiplyPathToo()
    {
        Copy(1, 1, sourceStride: 4, [64, 64, 64, 128], out var grey);
        Assert.True(grey);

        Copy(1, 1, sourceStride: 4, [64, 64, 65, 128], out var colour);
        Assert.False(colour);
    }

    [Fact]
    public void AMixedImageKeepsEachPixelsOwnAlpha()
    {
        byte[] source =
        [
            200, 100, 50, 255,
            200, 100, 50, 128,
            200, 100, 50, 0,
            200, 100, 50, 255
        ];

        var pixels = Copy(4, 1, sourceStride: 16, source, out _);

        Assert.Equal([200, 100, 50, 255], pixels[..4]);
        Assert.Equal([100, 50, 25, 128], pixels[4..8]);
        Assert.Equal([0, 0, 0, 0], pixels[8..12]);
        Assert.Equal([200, 100, 50, 255], pixels[12..16]);
    }

    private static byte[] Copy(int width, int height, int sourceStride, byte[] source, out bool isGrayscale)
    {
        var native = Marshal.AllocHGlobal(source.Length);

        try
        {
            Marshal.Copy(source, 0, native, source.Length);

            using var bitmap = new SKBitmap(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul));
            PixelCopy.CopyPremultiplyingRgba(native, sourceStride, bitmap, CancellationToken.None, out isGrayscale);

            using var pixmap = bitmap.PeekPixels();

            // Read the bytes as stored, not through GetPixel, which would unpremultiply them back.
            return pixmap.GetPixelSpan()[..(width * height * 4)].ToArray();
        }
        finally
        {
            Marshal.FreeHGlobal(native);
        }
    }
}