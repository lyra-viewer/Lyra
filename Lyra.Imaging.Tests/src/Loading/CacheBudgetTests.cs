using Lyra.Imaging.Loading;
using Xunit;

namespace Lyra.Imaging.Tests.Loading;

/// <summary>
/// How much decoded pixel data the cache may hold. A 24K EXR decodes to 1215 MB, so a fixed budget
/// small enough for modest machines cannot hold two neighbors at once - and re-decoding one costs
/// about three seconds, the exact cost the cache exists to avoid.
///
/// These pin the shape of the scaling: small machines unchanged, large ones capped.
/// </summary>
public class CacheBudgetTests
{
    private const long Gib = 1024L * 1024 * 1024;
    private const long OldFixedBudget = 1536L * 1024 * 1024;

    [Theory]
    [InlineData(4 * Gib)]
    [InlineData(8 * Gib)]
    [InlineData(12 * Gib)]
    public void SmallMachinesKeepTheOldBudget(long available)
    {
        Assert.Equal(OldFixedBudget, ImageLoader.ComputeCacheBudget(available));
    }

    [Fact]
    public void AMidSizedMachineGetsAnEighthOfIt()
    {
        Assert.Equal(4 * Gib, ImageLoader.ComputeCacheBudget(32 * Gib));
    }

    [Theory]
    [InlineData(128 * Gib)]
    [InlineData(512 * Gib)]
    public void LargeMachinesAreCapped(long available)
    {
        Assert.Equal(8 * Gib, ImageLoader.ComputeCacheBudget(available));
    }
    
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void AnUnknownMachineGetsTheFloor(long available)
    {
        Assert.Equal(OldFixedBudget, ImageLoader.ComputeCacheBudget(available));
    }
    
    [Fact]
    public void ARealisticMachineHoldsTheBiggestPairWeHaveMeasured()
    {
        const long twentyFourK = 1215L * 1024 * 1024;
        const long sixteenK = 575L * 1024 * 1024;

        Assert.True(ImageLoader.ComputeCacheBudget(16 * Gib) >= twentyFourK + sixteenK, "a 16 GB machine should hold the two largest measured images at once.");
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2 * Gib)]
    [InlineData(64 * Gib)]
    [InlineData(long.MaxValue)]
    public void TheBudgetIsAlwaysWithinItsBounds(long available)
    {
        var budget = ImageLoader.ComputeCacheBudget(available);

        Assert.InRange(budget, OldFixedBudget, 8 * Gib);
    }
}
