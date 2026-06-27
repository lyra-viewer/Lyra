using Lyra.FileLoader.Duplicates.Perceptual;
using Xunit;

namespace Lyra.Core.Tests.Duplicates;

public sealed class PerceptualHashTests
{
    [Fact]
    public void Compute_RejectsWrongLength()
    {
        Assert.Throws<ArgumentException>(() => PerceptualHash.Compute(new byte[PerceptualHash.SampleCount - 1]));
    }

    [Fact]
    public void Compute_UniformImage_IsNonZero()
    {
        // A flat image yields an all-zero dHash; the "computed" marker must keep it
        // distinct from the "not computed" sentinel (PHash == 0).
        var hash = PerceptualHash.Compute(new byte[PerceptualHash.SampleCount]);
        Assert.NotEqual(0UL, hash);
    }

    [Fact]
    public void Compute_StrictGradient_SetsAllComparisonBits()
    {
        // Every pixel is strictly less than its right neighbour → every comparison bit set.
        var gradient = new byte[PerceptualHash.SampleCount];
        for (var y = 0; y < PerceptualHash.Height; y++)
        for (var x = 0; x < PerceptualHash.Width; x++)
            gradient[y * PerceptualHash.Width + x] = (byte)(x * 10);

        Assert.Equal(ulong.MaxValue, PerceptualHash.Compute(gradient));
    }

    [Fact]
    public void Distance_UnrelatedLowDetailImages_ExceedDefaultThreshold()
    {
        // Two visually unrelated low-detail images (a near-flat infographic and a gradient)
        // whose sparse dHashes previously false-matched at distance 10. The default must reject them.
        const ulong a = 9232660885737046272;
        const ulong b = 9223666776837980160;

        Assert.Equal(10, PerceptualHash.Distance(a, b));
        Assert.True(PerceptualHash.Distance(a, b) > PerceptualDuplicateFinder.DefaultMaxDistance);
    }

    [Fact]
    public void Distance_IsZeroForIdenticalAndIgnoresMarkerBit()
    {
        var uniform = PerceptualHash.Compute(new byte[PerceptualHash.SampleCount]);

        var gradient = new byte[PerceptualHash.SampleCount];
        for (var y = 0; y < PerceptualHash.Height; y++)
        for (var x = 0; x < PerceptualHash.Width; x++)
            gradient[y * PerceptualHash.Width + x] = (byte)(x * 10);
        
        var full = PerceptualHash.Compute(gradient);

        Assert.Equal(0, PerceptualHash.Distance(uniform, uniform));
        // 64 comparison bits differ except the shared top marker bit → 63.
        Assert.Equal(63, PerceptualHash.Distance(uniform, full));
    }
}
