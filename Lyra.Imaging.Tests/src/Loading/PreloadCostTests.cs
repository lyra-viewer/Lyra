using Lyra.Common.Estimation;
using Lyra.Imaging.Loading;
using Xunit;

namespace Lyra.Imaging.Tests.Loading;

/// <summary>Which neighbors are worth fetching before they are asked for.</summary>
public class PreloadCostTests
{
    private const long MB = 1024 * 1024;

    private static readonly TransferEstimate SlowShare = new(LatencyMs: 20, BytesPerMs: 11.0 * MB / 1000);

    private static readonly TransferEstimate FastDisk = new(LatencyMs: 0.1, BytesPerMs: 2000.0 * MB / 1000);

    [Fact]
    public void AnOrdinaryPhotoOnASlowShareIsPreloaded()
    {
        Assert.True(ImageLoader.WorthPreloading(12 * MB, SlowShare, out _));
    }

    [Fact]
    public void AMultiGigabyteFileOnASlowShareIsNot()
    {
        Assert.False(ImageLoader.WorthPreloading(4300 * MB, SlowShare, out var expectedMs));
        Assert.True(expectedMs > 60_000, $"4.3 GB at 11 MB/s should be minutes, estimated {expectedMs:F0} ms");
    }

    [Fact]
    public void TheSameFileOnAFastDiskIs()
    {
        Assert.True(ImageLoader.WorthPreloading(4300 * MB, FastDisk, out _));
    }

    [Fact]
    public void AnUnmeasuredSourceStillPreloadsOrdinaryFiles()
    {
        Assert.True(ImageLoader.WorthPreloading(50 * MB, null, out _));
    }

    [Fact]
    public void AnUnmeasuredSourceDoesNotGambleOnHugeOnes()
    {
        Assert.False(ImageLoader.WorthPreloading(753 * MB, null, out _));
    }
}
