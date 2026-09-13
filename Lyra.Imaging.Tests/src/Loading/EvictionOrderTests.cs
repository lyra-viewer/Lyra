using Lyra.Imaging.Loading;
using Xunit;

namespace Lyra.Imaging.Tests.Loading;

/// <summary>
/// Which cached image goes first when the budget is exceeded.
/// </summary>
public class EvictionOrderTests
{
    private const long Mb = 1024L * 1024;

    private static ImageLoader.EvictionCandidate Candidate(string name, int distance, long megabytes)
        => new(name, distance, megabytes * Mb);

    private static string[] Order(params ImageLoader.EvictionCandidate[] candidates)
        => ImageLoader.EvictionOrder(candidates).Select(c => c.Path).ToArray();
    
    [Fact]
    public void TheMeasuredCase_DropsTheExpensiveOnesAndSparesTheIcon()
    {
        var order = Order(
            Candidate("qwantani_24k.exr", 1, 1215),
            Candidate("hilly_16k.exr", 2, 575),
            Candidate("resting_4k.exr", 3, 64),
            Candidate("icon.icns", 4, 5)
        );

        Assert.Equal(["qwantani_24k.exr", "hilly_16k.exr", "resting_4k.exr", "icon.icns"], order);
    }

    [Fact]
    public void ASmallFarFileOutlivesALargeCloseOne()
    {
        var order = Order(Candidate("huge.exr", 1, 1200), Candidate("icon.icns", 6, 5));

        Assert.Equal("huge.exr", order[0]);
    }

    [Fact]
    public void EqualSizesGoFromTheFarEnd()
    {
        var order = Order(Candidate("near.exr", 1, 500), Candidate("far.exr", 4, 500));

        Assert.Equal(["far.exr", "near.exr"], order);
    }

    [Fact]
    public void EqualDistancesGoLargestFirst()
    {
        var order = Order(Candidate("small.exr", 2, 100), Candidate("large.exr", 2, 900));

        Assert.Equal(["large.exr", "small.exr"], order);
    }

    [Fact]
    public void DistanceCanOutweighASmallSizeDifference()
    {
        var order = Order(Candidate("neighbour.exr", 1, 1215), Candidate("distant.exr", 4, 575));

        Assert.Equal("distant.exr", order[0]);
    }

    [Fact]
    public void NothingResidentMeansNothingToEvict()
    {
        Assert.Empty(ImageLoader.EvictionOrder([]));
    }

    [Fact]
    public void TheOrderIsAPermutationOfTheCandidates()
    {
        var candidates = new[]
        {
            Candidate("a", 0, 10),
            Candidate("b", 3, 10),
            Candidate("c", 2, 900),
            Candidate("d", 1, 900)
        };

        var order = ImageLoader.EvictionOrder(candidates);

        Assert.Equal(candidates.Length, order.Count);
        Assert.Equal(candidates.OrderBy(c => c.Path), order.OrderBy(c => c.Path));
    }
}