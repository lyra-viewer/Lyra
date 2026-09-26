using Lyra.Imaging.Content;
using Xunit;

namespace Lyra.Imaging.Tests.Loading;

/// <summary>
/// The transfer/decode split only means anything while both halves describe the same stretch of
/// time. Reading outlives the load - a document's later pages, a sheet's tiles - so the guard that
/// keeps those out of the total is what stops the decode figure from decaying as one is browsed.
/// </summary>
public class LoadMeasurementTests
{
    private static LoadMeasurement Loading()
    {
        var timing = new LoadMeasurement();
        timing.Begin();
        return timing;
    }

    [Fact]
    public void TransferDuringTheLoadIsCounted()
    {
        var timing = Loading();

        timing.CompleteTransfer(1000, 25);
        timing.MarkComplete();

        Assert.True(timing.TransferMeasured);
        Assert.Equal(25, timing.TransferMs!.Value, precision: 3);
        Assert.Equal(1000, timing.TransferBytesRead);
    }

    [Fact]
    public void TransferAfterTheLoadFinishedIsIgnored()
    {
        var timing = Loading();

        timing.CompleteTransfer(1000, 5);
        Thread.Sleep(40); // the load must outlast its own transfer, or decode is legitimately zero
        timing.MarkComplete();

        var transferBefore = timing.TransferMs!.Value;
        var decodeBefore = timing.DecodeMs!.Value;
        var bytesBefore = timing.TransferBytesRead;

        timing.CompleteTransfer(9_000_000, 30_000);
        timing.ReportTransferred(9_000_000);

        Assert.Equal(transferBefore, timing.TransferMs!.Value, precision: 3);
        Assert.Equal(decodeBefore, timing.DecodeMs!.Value, precision: 3);
        Assert.Equal(bytesBefore, timing.TransferBytesRead);

        // The point of all of it: decode does not collapse to nothing.
        Assert.True(timing.DecodeMs > 0, "a late read should not swallow the decode figure");
    }

    [Fact]
    public void TransferBeforeTheLoadStartedIsIgnored()
    {
        var timing = new LoadMeasurement();

        timing.CompleteTransfer(1000, 25);

        Assert.False(timing.TransferMeasured);
        Assert.Null(timing.TransferMs);
        Assert.Equal(0, timing.TransferBytesRead);
    }

    [Fact]
    public void BeginningAgainCountsAfresh()
    {
        var timing = Loading();

        timing.CompleteTransfer(1000, 25);
        timing.MarkComplete();
        timing.CompleteTransfer(5000, 999); // ignored, the load is over

        timing.Begin();

        Assert.False(timing.TransferMeasured);

        timing.CompleteTransfer(2000, 40);
        timing.MarkComplete();

        Assert.Equal(40, timing.TransferMs!.Value, precision: 3);
        Assert.Equal(2000, timing.TransferBytesRead);
    }

    [Fact]
    public void TimeAPreloadSpentPausedIsNotLearnedAsDecode()
    {
        var timing = Loading();

        timing.CompleteTransfer(1000, 5);
        Thread.Sleep(60);
        timing.AddPause(50);
        timing.MarkComplete();

        Assert.Equal(50, timing.PausedMs, precision: 3);
        Assert.Equal(timing.CompleteMs!.Value - 5 - 50, timing.DecodeMs!.Value, precision: 3);
    }

    [Fact]
    public void APauseBeforeTheLoadBeganIsIgnored()
    {
        var timing = new LoadMeasurement();

        timing.AddPause(500);
        timing.Begin();
        timing.MarkComplete();

        Assert.Equal(0, timing.PausedMs);
    }
}
