using Lyra.Imaging.Decoding.Support;
using SkiaSharp;
using Xunit;

namespace Lyra.Imaging.Tests.Decoding;

/// <summary>
/// Covers the pixel permutation behind EXIF orientation. The expected result is stated as an
/// inverse mapping - for each destination pixel, which source pixel it must come from - which
/// is derived independently of the forward coefficients the implementation uses, so a sign or
/// axis slip in either one shows up as a failure rather than cancelling out.
/// </summary>
public class OrientationTransformTests
{
    // Deliberately non-square, so a transposed result cannot accidentally look correct.
    private const int Width = 5;
    private const int Height = 3;

    [Theory]
    [InlineData(SKEncodedOrigin.TopRight)]
    [InlineData(SKEncodedOrigin.BottomRight)]
    [InlineData(SKEncodedOrigin.BottomLeft)]
    [InlineData(SKEncodedOrigin.LeftTop)]
    [InlineData(SKEncodedOrigin.RightTop)]
    [InlineData(SKEncodedOrigin.RightBottom)]
    [InlineData(SKEncodedOrigin.LeftBottom)]
    public void Apply_MapsEveryPixelToItsSpecifiedPosition(SKEncodedOrigin origin)
    {
        var source = CreateGradient();
        var expected = Snapshot(source);

        using var result = OrientationTransform.Apply(source, origin);

        var swaps = OrientationTransform.SwapsAxes(origin);
        Assert.Equal(swaps ? Height : Width, result.Width);
        Assert.Equal(swaps ? Width : Height, result.Height);

        for (var dy = 0; dy < result.Height; dy++)
        for (var dx = 0; dx < result.Width; dx++)
        {
            var (sx, sy) = SourceOf(origin, dx, dy);
            Assert.Equal(expected[sx, sy], result.GetPixel(dx, dy));
        }
    }

    [Fact]
    public void Apply_TopLeft_ReturnsTheSameBitmapUntouched()
    {
        using var source = CreateGradient();

        var result = OrientationTransform.Apply(source, SKEncodedOrigin.TopLeft);

        Assert.Same(source, result);
    }

    [Fact]
    public void Apply_PreservesAlphaTypeAndColorSpace()
    {
        var info = new SKImageInfo(Width, Height, SKColorType.Rgba8888, SKAlphaType.Unpremul, SKColorSpace.CreateSrgbLinear());
        var source = new SKBitmap(info);

        using var result = OrientationTransform.Apply(source, SKEncodedOrigin.RightTop);

        Assert.Equal(SKAlphaType.Unpremul, result.AlphaType);
        Assert.True(SKColorSpace.Equal(SKColorSpace.CreateSrgbLinear(), result.ColorSpace));
    }

    [Fact]
    public void Apply_UnsupportedColorType_LeavesTheBitmapAlone()
    {
        // Gray8 never reaches this code today; the guard exists so that if it ever does,
        // the pixels are left readable instead of being reinterpreted as 32-bit.
        using var source = new SKBitmap(new SKImageInfo(Width, Height, SKColorType.Gray8, SKAlphaType.Opaque));

        var result = OrientationTransform.Apply(source, SKEncodedOrigin.RightTop);

        Assert.Same(source, result);
    }

    /// <summary>For a destination pixel, the source pixel it must have come from.</summary>
    private static (int x, int y) SourceOf(SKEncodedOrigin origin, int dx, int dy) => origin switch
    {
        SKEncodedOrigin.TopLeft     => (dx, dy),
        SKEncodedOrigin.TopRight    => (Width - 1 - dx, dy),
        SKEncodedOrigin.BottomRight => (Width - 1 - dx, Height - 1 - dy),
        SKEncodedOrigin.BottomLeft  => (dx, Height - 1 - dy),
        SKEncodedOrigin.LeftTop     => (dy, dx),
        SKEncodedOrigin.RightTop    => (dy, Height - 1 - dx),
        SKEncodedOrigin.RightBottom => (Width - 1 - dy, Height - 1 - dx),
        SKEncodedOrigin.LeftBottom  => (Width - 1 - dy, dx),
        _ => throw new ArgumentOutOfRangeException(nameof(origin))
    };

    /// <summary>Every pixel gets a unique color, so any misplacement is detectable.</summary>
    private static SKBitmap CreateGradient()
    {
        var bitmap = new SKBitmap(new SKImageInfo(Width, Height, SKColorType.Rgba8888, SKAlphaType.Unpremul));

        for (var y = 0; y < Height; y++)
        for (var x = 0; x < Width; x++)
            bitmap.SetPixel(x, y, new SKColor((byte)(10 + x * 40), (byte)(10 + y * 40), 200));

        return bitmap;
    }

    /// <summary>Apply() disposes its source, so the expected pixels are captured up front.</summary>
    private static SKColor[,] Snapshot(SKBitmap bitmap)
    {
        var pixels = new SKColor[bitmap.Width, bitmap.Height];

        for (var y = 0; y < bitmap.Height; y++)
        for (var x = 0; x < bitmap.Width; x++)
            pixels[x, y] = bitmap.GetPixel(x, y);

        return pixels;
    }
}