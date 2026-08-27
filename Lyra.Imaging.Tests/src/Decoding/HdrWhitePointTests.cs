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
