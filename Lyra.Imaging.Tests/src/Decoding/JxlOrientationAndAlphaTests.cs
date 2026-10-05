using Lyra.Imaging.Content;
using Lyra.Imaging.Decoding.Decoders;
using Lyra.Imaging.Tests.Support;
using SkiaSharp;
using Xunit;

namespace Lyra.Imaging.Tests.Decoding;

/// <summary>
/// Orientation and premultiplied alpha, which the native wrapper resolves for the still and the
/// animated path alike. Fixtures come from libjxl's encoder; skipped without the native wrapper.
/// </summary>
public class JxlOrientationAndAlphaTests
{
    // Stored 4x2 with its left column red and the rest blue, oriented "rotate 90 CW": shown 2x4, red on top.
    private const string Rotated = "/woIcAusCQgAEABgAEsYixXCEcpuBXoXEAFA/rct9Bn1Isg1AA==";

    // The same, as an animation whose second frame is solid green.
    private const string RotatedAnimation = "/woIcEsA1gQIAKACAABgAEsYixXCEcpuBXoXEAFA/rct9Bn1Isg1AAgAoEIAADgASxiLFcJJQQ5AfwBAfwA=";

    // 2x2 of straight (255, 0, 0, 128), stored premultiplied as (128, 0, 0, 128).
    private const string PremultipliedAlpha = "/woIEDAASggAEAAwAEsYixXCSUFOAAAEAA==";

    // The same, as a two-frame animation.
    private const string PremultipliedAlphaAnimation = "/woIEEEABkATCACgAgAAMABLGIsVwklBTgAABAAIAKBCAAAwAEsYixXCSUFOAAAEAA==";

    private static readonly Lazy<bool> NativeJxlReady = new(() => NativeWrapper.TryLoad("libjxl_native", typeof(JxlDecoder).Assembly));

    [Fact]
    public void ARotatedImage_IsShownUpright()
    {
        WithDecoded(Rotated, composite =>
        {
            var raster = Assert.IsType<RasterContent>(composite.Content);

            Assert.Equal((2, 4), (raster.Image.Width, raster.Image.Height));
            AssertColor(255, 0, 0, Srgb(raster, 0, 0));
            AssertColor(255, 0, 0, Srgb(raster, 1, 0));
            AssertColor(0, 0, 255, Srgb(raster, 0, 3));
        });
    }

    [Fact]
    public void ARotatedAnimation_IsShownUpright()
    {
        WithDecoded(RotatedAnimation, composite =>
        {
            var set = Assert.IsType<VariantRasterContent>(composite.Content);
            Assert.All(set.Variants, v => Assert.Equal((2, 4), (v.Width, v.Height)));

            var raster = Assert.IsType<RasterContent>(set.Active);
            Assert.Equal((2, 4), (raster.Image.Width, raster.Image.Height));
            AssertColor(255, 0, 0, Srgb(raster, 1, 0));
            AssertColor(0, 0, 255, Srgb(raster, 1, 3));
        });
    }

    [Theory]
    [InlineData(PremultipliedAlpha)]
    [InlineData(PremultipliedAlphaAnimation)]
    public void PremultipliedAlpha_IsPremultipliedOnce(string fixture)
    {
        WithDecoded(fixture, composite =>
        {
            var raster = Assert.IsType<RasterContent>(composite.Content is VariantRasterContent set ? set.Active : composite.Content);
            var pixel = Srgb(raster, 1, 1);

            Assert.InRange(pixel.Alpha, 126, 130);
            AssertColor(255, 0, 0, pixel);
        });
    }

    private static void AssertColor(byte red, byte green, byte blue, SKColor actual) =>
        Assert.True(Math.Abs(actual.Red - red) <= 6 && Math.Abs(actual.Green - green) <= 6 && Math.Abs(actual.Blue - blue) <= 6, $"expected ({red},{green},{blue}), got {actual}");

    /// <summary>The unpremultiplied pixel in sRGB, since SDR frames arrive in Display-P3.</summary>
    private static SKColor Srgb(RasterContent raster, int x, int y)
    {
        var info = new SKImageInfo(1, 1, SKColorType.Rgba8888, SKAlphaType.Unpremul, SKColorSpace.CreateSrgb());

        using var pixel = new SKBitmap(info);
        Assert.True(raster.Image.ReadPixels(info, pixel.GetPixels(), info.RowBytes, x, y));
        return pixel.GetPixel(0, 0);
    }

    private static void WithDecoded(string base64, Action<Composite> assert)
    {
        if (!NativeJxlReady.Value)
            Assert.Skip("libjxl_native not available (native wrappers not built for this platform).");

        using var file = new TempFile(Convert.FromBase64String(base64));
        using var composite = new Composite(new FileInfo(file.Path));

        new JxlDecoder().DecodeAsync(composite, CancellationToken.None).GetAwaiter().GetResult();

        assert(composite);
    }
}