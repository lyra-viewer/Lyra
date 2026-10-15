using Lyra.Common.Settings.Enums;
using Lyra.Imaging.Content;
using Lyra.Imaging.Decoding.Decoders.Raw;
using Lyra.Imaging.Decoding.Support;
using SkiaSharp;
using Xunit;

namespace Lyra.Imaging.Tests.Decoding;

public class RawExposureTests
{
    [Theory]
    [InlineData(0.148f, 0.0388f)]
    [InlineData(0.236f, 0.1685f)]
    [InlineData(0.043f, 0.0213f)]
    public void TheMatchedGain_PutsTheMedianWhereThePreviewHasIt(float previewMedian, float rawMedian)
    {
        var stops = RawExposure.MatchStops(previewMedian, rawMedian);

        Assert.NotNull(stops);
        Assert.Equal(previewMedian, RawExposure.Displayed(stops.Value, rawMedian), 0.001f);
    }

    [Fact]
    public void ADarkerRaw_GetsMoreGain()
    {
        var bright = RawExposure.MatchStops(0.18f, 0.10f)!.Value;
        var dark = RawExposure.MatchStops(0.18f, 0.05f)!.Value;

        Assert.True(dark > bright, $"{dark} should exceed {bright}");
    }

    [Fact]
    public void TheGain_StaysWithinItsBounds()
    {
        Assert.Equal(RawExposure.MaxStops, RawExposure.MatchStops(0.5f, 0.0001f));
        Assert.Equal(RawExposure.MinStops, RawExposure.MatchStops(0.01f, 0.9f));
    }

    [Theory]
    [InlineData(0f, 0.1f)]
    [InlineData(0.2f, 0f)]
    [InlineData(float.NaN, 0.1f)]
    public void ABlackOrBrokenMedian_LeavesTheDefault(float previewMedian, float rawMedian)
    {
        Assert.Null(RawExposure.MatchStops(previewMedian, rawMedian));
    }

    [Fact]
    public void SaturationLandsOnWhite_UnderTheDefaultCurve()
    {
        var range = RawExposure.RangeFor(1.2f);

        Assert.Equal(255, ToneMapped(range.SceneWhite!.Value, range));
        Assert.True(ToneMapped(range.SceneWhite.Value, default) < 250, "without the range, ACES leaves saturation grey");
    }

    [Theory]
    [InlineData(ToneMapMode.Aces, 0.75f)]
    [InlineData(ToneMapMode.ReinhardExtended, 0.75f)]
    [InlineData(ToneMapMode.Clip, 0.75f)]
    [InlineData(ToneMapMode.ReinhardExtended, 2.5f)]
    [InlineData(ToneMapMode.Clip, 2.5f)]
    public void SaturationLandsOnWhite_UnderEveryCurve(ToneMapMode mode, float sceneWhite)
    {
        var range = new SceneRange(SceneWhite: sceneWhite, Peak: RawExposure.Peak);

        Assert.Equal(255, ToneMapped(sceneWhite, range, mode));
        Assert.True(ToneMapped(sceneWhite / 4f, range, mode) < 250, "light below saturation should stay below white");
    }

    [Fact]
    public void TheCamerasOwnCurve_ReplacesTheDefaultOne()
    {
        using var curve = new CameraToneCurve([.. Enumerable.Range(0, 1024).Select(i => MathF.Pow(i / 1023f, 2f))]);
        var range = new SceneRange(Curve: curve);

        // Grey stays grey through ProPhoto and back: 0.5 squared is 0.25, which sRGB encodes as 137.
        Assert.InRange(ToneMapped(0.5f, range), 136, 138);
        Assert.Equal(255, ToneMapped(1f, range));

        // Only in the default curve's place.
        Assert.Equal(188, ToneMapped(0.5f, range, ToneMapMode.Clip));
    }

    [Fact]
    public void AMedianPreviewIsReadAsLight()
    {
        using var preview = new SKBitmap(new SKImageInfo(4, 4, SKColorType.Rgba8888, SKAlphaType.Opaque, SKColorSpace.CreateSrgb()));
        preview.Erase(new SKColor(188, 188, 188)); // sRGB-encoded 0.5

        Assert.Equal(0.5f, RawExposure.MedianLuminance(preview)!.Value, 0.01f);
    }

    private static unsafe byte ToneMapped(float light, SceneRange range, ToneMapMode mode = ToneMapMode.Aces)
    {
        float[] pixel = [light, light, light, 1f];
        using var bitmap = new SKBitmap(new SKImageInfo(1, 1, SKColorType.Rgba8888, SKAlphaType.Unpremul, SKColorSpace.CreateSrgb()));

        fixed (float* p = pixel)
            HdrToneMap.ToBitmap(new FloatRgbaRows(p, 1, 1), bitmap, mode, 1f, range, CancellationToken.None, out _);

        return bitmap.GetPixel(0, 0).Red;
    }
}
