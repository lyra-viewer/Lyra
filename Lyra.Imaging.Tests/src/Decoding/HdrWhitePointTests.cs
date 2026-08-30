using Lyra.Imaging.Content;
using Lyra.Imaging.Decoding.Support;
using Lyra.Imaging.Tests.Support;
using Xunit;

namespace Lyra.Imaging.Tests.Decoding;

public class HdrWhitePointTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData(0.001)]  // a sparse bright tail, like a sun
    [InlineData(0.05)]   // a broad one, like a blown window
    public void SubsampledWhitePoint_MatchesAnExhaustiveScan(double tailFraction)
    {
        const int pixels = 4096 * 2048;
        var rgba = new float[(long)pixels * 4];
        var random = new Random(11);

        for (var i = 0; i < pixels; i++)
        {
            // Most of the scene sits around diffuse white, with a small fraction far above it.
            var value = random.NextDouble() < tailFraction
                ? 200f + (float)(random.NextDouble() * 40_000)
                : (float)(random.NextDouble() * 3.0);

            rgba[(i * 4) + 0] = value;
            rgba[(i * 4) + 1] = value;
            rgba[(i * 4) + 2] = value;
            rgba[(i * 4) + 3] = 1f;
        }

        var fast = HdrProfileAccess.MeasureWhitePoint(rgba);
        var exhaustive = ExhaustivePercentile(rgba);

        output.WriteLine($"tail {tailFraction:P1}   subsampled {fast:F1}   exhaustive {exhaustive:F1}");

        // The histogram has 512 bins across 36 stops, so adjacent bins are ~5% apart. Landing
        // within a bin is exact agreement as far as the output can express.
        Assert.InRange(fast, exhaustive * 0.94f, exhaustive * 1.07f);
    }
    
    [Fact]
    public void TheMeasuredRangeIsReported()
    {
        var composite = new Composite(new FileInfo("bright.exr"));

        using var content = HdrImageBuilder.Build(Ramp(8, 8, top: 12f), 8, 8, composite, CancellationToken.None, out _);

        var measured = Assert.IsType<HdrRasterContent>(content).WhitePoint;
        var row = Row(composite, "Dynamic Range");

        Assert.Equal($"{measured:0.0}x SDR white ({MathF.Log2(measured):0.0} stops)", row);
        Assert.True(measured > 1f, $"the ramp reaches well above white, but measured {measured}.");
    }
    
    [Fact]
    public void AnImageWithNothingAboveWhite_SaysSoInWords()
    {
        var composite = new Composite(new FileInfo("dim.exr"));

        using var content = HdrImageBuilder.Build(Ramp(8, 8, top: 0.9f), 8, 8, composite, CancellationToken.None, out _);

        Assert.Equal("Within SDR white", Row(composite, "Dynamic Range"));
    }
    
    [Fact]
    public void AVeryBrightImageIsStatedWithoutADecimal()
    {
        var composite = new Composite(new FileInfo("sun.exr"));

        using var content = HdrImageBuilder.Build(Ramp(8, 8, top: 40_000f), 8, 8, composite, CancellationToken.None, out _);

        var row = Row(composite, "Dynamic Range");

        Assert.DoesNotContain(".", row.Split('x')[0]);
        Assert.DoesNotContain(",", row.Split('x')[0]);
        Assert.Contains("stops", row);
    }

    private static string Row(Composite composite, string key)
    {
        var rows = composite.FormatSpecificSnapshot();

        Assert.Contains(rows, row => row.Key == key);
        return rows.First(row => row.Key == key).Value;
    }
    
    private static float[] Ramp(int width, int height, float top)
    {
        var rgba = new float[width * height * 4];
        var pixels = width * height;

        for (var i = 0; i < pixels; i++)
        {
            var value = top * (i + 1) / pixels;

            rgba[(i * 4) + 0] = value;
            rgba[(i * 4) + 1] = value;
            rgba[(i * 4) + 2] = value;
            rgba[(i * 4) + 3] = 1f;
        }

        return rgba;
    }

    /// <summary>The 99.9th percentile by an exact sort - the oracle the histogram approximates.</summary>
    private static float ExhaustivePercentile(float[] rgba)
    {
        var luminance = new List<float>(rgba.Length / 4);

        for (var i = 0; i + 3 < rgba.Length; i += 4)
        {
            var l = (0.2126f * rgba[i]) + (0.7152f * rgba[i + 1]) + (0.0722f * rgba[i + 2]);
            if (float.IsFinite(l) && l > 0)
                luminance.Add(l);
        }

        luminance.Sort();

        var index = (int)(luminance.Count * 0.999);
        return MathF.Max(luminance[Math.Min(index, luminance.Count - 1)], 1f);
    }
}
