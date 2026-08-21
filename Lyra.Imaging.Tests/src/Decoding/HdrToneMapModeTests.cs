using Lyra.Common.Settings.Enums;
using Lyra.Imaging.Decoding.Support;
using SkiaSharp;
using Xunit;

namespace Lyra.Imaging.Tests.Decoding;

/// <summary>
/// The three tone-mapping curves, checked on the case that motivated making it selectable: a
/// small very bright source (a sun at ~40000) sitting in an already-bright sky (~2.7).
///
/// Under ACES the whole 14,600:1 range lands inside a handful of 8-bit levels, so the sun is
/// white on white - correct highlight roll-off, but it does not read as a sun. These tests pin
/// each curve's behavior so a future change to one cannot quietly flatten another.
/// </summary>
public class HdrToneMapModeTests
{
    private const float Sky = 2.7f;
    private const float Sun = 40000f;

    /// <summary>Tone-maps a one-row image and returns the resulting red channel per pixel.</summary>
    private static byte[] Map(ToneMapMode mode, params float[] linearValues) => Map(mode, 0, linearValues);

    /// <summary>Tone-maps a one-row image at the given exposure and returns the red channel.</summary>
    private static byte[] Map(ToneMapMode mode, int exposureStops, params float[] linearValues)
    {
        var pixels = new float[linearValues.Length * 4];
        for (var i = 0; i < linearValues.Length; i++)
        {
            pixels[(i * 4) + 0] = linearValues[i];
            pixels[(i * 4) + 1] = linearValues[i];
            pixels[(i * 4) + 2] = linearValues[i];
            pixels[(i * 4) + 3] = 1f;
        }

        var info = new SKImageInfo(linearValues.Length, 1, SKColorType.Rgba8888, SKAlphaType.Unpremul);
        using var bitmap = new SKBitmap(info);

        HdrToneMap.ToBitmap(pixels, bitmap, mode, MathF.Pow(2f, exposureStops), CancellationToken.None, out _);

        return Enumerable.Range(0, linearValues.Length)
            .Select(x => bitmap.GetPixel(x, 0).Red)
            .ToArray();
    }

    [Fact]
    public void Aces_CompressesSunAndSkyIntoAlmostNothing()
    {
        var result = Map(ToneMapMode.Aces, Sky, Sun);

        // Both are essentially white; this is the behavior that hides the sun.
        Assert.True(result[1] - result[0] < 10, $"expected ACES to flatten sky ({result[0]}) and sun ({result[1]}) together");
        Assert.Equal(255, result[1]);
    }

    [Fact]
    public void ReinhardExtended_KeepsSunSeparatedFromSky()
    {
        var result = Map(ToneMapMode.ReinhardExtended, Sky, Sun);

        Assert.True(result[1] - result[0] > 25, $"expected clear separation, got sky={result[0]} sun={result[1]}");
        Assert.Equal(255, result[1]);
    }

    [Fact]
    public void Clip_BlowsOutEverythingAboveOne()
    {
        var result = Map(ToneMapMode.Clip, 0.5f, 1f, Sky, Sun);

        Assert.True(result[0] < 255);
        Assert.Equal(255, result[1]);
        Assert.Equal(255, result[2]); // the sky clips too, so the sun reads as one flat mass
        Assert.Equal(255, result[3]);
    }

    [Theory]
    [InlineData(ToneMapMode.Aces)]
    [InlineData(ToneMapMode.ReinhardExtended)]
    [InlineData(ToneMapMode.Clip)]
    public void EveryMode_IsMonotonic_AndAnchoredAtBlack(ToneMapMode mode)
    {
        var result = Map(mode, 0f, 0.05f, 0.18f, 0.5f, 1f, 4f, 100f, 10000f);

        Assert.Equal(0, result[0]);

        for (var i = 1; i < result.Length; i++)
            Assert.True(result[i] >= result[i - 1], $"{mode} is not monotonic at index {i}: {result[i - 1]} then {result[i]}");
    }

    [Theory]
    [InlineData(ToneMapMode.Aces)]
    [InlineData(ToneMapMode.ReinhardExtended)]
    [InlineData(ToneMapMode.Clip)]
    public void EveryMode_SurvivesNaNInfinityAndNegatives(ToneMapMode mode)
    {
        // Real EXRs carry all three. NaN is black by convention; infinity must be white, not the
        // black it used to become when the ACES rational hit inf/inf.
        var result = Map(mode, float.NaN, float.PositiveInfinity, -5f, 1e30f);

        Assert.Equal(0, result[0]);
        Assert.Equal(255, result[1]);
        Assert.Equal(0, result[2]);
        Assert.Equal(255, result[3]);
    }

    [Theory]
    [InlineData(ToneMapMode.Aces)]
    [InlineData(ToneMapMode.ReinhardExtended)]
    [InlineData(ToneMapMode.Clip)]
    public void Exposure_BrightensAndDarkens_UnderEveryCurve(ToneMapMode mode)
    {
        // One stop up doubles the light reaching the curve, one stop down halves it. Measured at
        // 0.1 linear, well below where any of the curves has flattened, so the change is visible.
        var darker = Map(mode, -1, 0.1f)[0];
        var normal = Map(mode, 0, 0.1f)[0];
        var brighter = Map(mode, 1, 0.1f)[0];

        Assert.True(darker < normal, $"{mode}: -1 stop ({darker}) should be darker than 0 ({normal})");
        Assert.True(brighter > normal, $"{mode}: +1 stop ({brighter}) should be brighter than 0 ({normal})");
    }

    [Fact]
    public void Exposure_LiftsTheSunOutOfTheSky_UnderAces()
    {
        // ACES flattens sun into sky at native exposure. Pulling exposure down moves both back
        // onto the curve's slope, which is what the slider is for when the curve alone cannot
        // separate them.
        var native = Map(ToneMapMode.Aces, 0, Sky, Sun);
        var pulled = Map(ToneMapMode.Aces, -6, Sky, Sun);

        Assert.True(native[1] - native[0] < 10);
        Assert.True(pulled[1] - pulled[0] > native[1] - native[0], $"exposure should widen the gap: native {native[1] - native[0]}, pulled {pulled[1] - pulled[0]}");
    }

    [Fact]
    public void ReinhardWhitePoint_IgnoresASingleHotPixel()
    {
        // The white point comes from a high percentile, so one stray sample cannot re-grade the
        // image and drag everything else into the shadows.
        var ordinary = Enumerable.Repeat(1f, 2000).ToArray();

        var withoutOutlier = Map(ToneMapMode.ReinhardExtended, ordinary);
        var withOutlier = Map(ToneMapMode.ReinhardExtended, [..ordinary, 1e6f]);

        Assert.True(Math.Abs(withOutlier[0] - withoutOutlier[0]) <= 2, $"one hot pixel shifted the grade: {withoutOutlier[0]} then {withOutlier[0]}");
    }
}
