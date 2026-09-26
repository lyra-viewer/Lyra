using Lyra.Common.Estimation;
using Lyra.Renderer.GUI.Presenters;
using Xunit;

namespace Lyra.Core.Tests.Rendering;

public class LoadProgressPresenterTests
{
    private const long MB = 1024 * 1024;

    // 10 MB/s: 100 MB takes 10 s to transfer, plus 1 s to decode.
    private static readonly TransferEstimate Share = new(LatencyMs: 0, BytesPerMs: 10.0 * MB / 1000);

    private static readonly object File = new();

    private static LoadSnapshot At(double elapsedMs, long read, TransferEstimate? source = null, double decodeMs = 1000) =>
        new(File, Active: true, ElapsedMs: elapsedMs, DecodeEstimateMs: decodeMs,
            BytesTotal: 100 * MB, BytesRead: read, Source: source);

    [Fact]
    public void WhileBytesArrive_TheBarFollowsThem_NotTheClock()
    {
        var bar = new LoadProgressPresenter();

        var progress = bar.Update(At(elapsedMs: 9000, read: 20 * MB, Share));

        Assert.False(progress.Indeterminate);
        Assert.Equal(10f / 11 * 0.2f, progress.Value, precision: 3);
    }

    [Fact]
    public void BeforeTheSourceIsMeasured_TheBarStillFollowsTheBytes()
    {
        var bar = new LoadProgressPresenter();

        var progress = bar.Update(At(elapsedMs: 10_000, read: 50 * MB, source: null));

        Assert.False(progress.Indeterminate);
        Assert.Equal(20f / 21 * 0.5f, progress.Value, precision: 3);
    }

    [Fact]
    public void WithNoDecodeEstimate_TheBytesAreTheWholeBar()
    {
        var bar = new LoadProgressPresenter();

        var progress = bar.Update(At(elapsedMs: 3000, read: 30 * MB, Share, decodeMs: 0));

        Assert.False(progress.Indeterminate);
        Assert.Equal(0.3f, progress.Value, precision: 3);
    }

    [Fact]
    public void BeforeAnyBytesAreReported_TheClockDrivesIt()
    {
        var bar = new LoadProgressPresenter();

        var progress = bar.Update(At(elapsedMs: 5500, read: 0, Share));

        Assert.Equal(0.5f, progress.Value, precision: 3);
    }

    [Fact]
    public void TheFirstBytes_ReplaceWhatTheClockGuessed()
    {
        var bar = new LoadProgressPresenter();

        bar.Update(At(elapsedMs: 5500, read: 0, Share));
        var bytes = bar.Update(At(elapsedMs: 6000, read: 10 * MB, Share));

        Assert.Equal(10f / 11 * 0.1f, bytes.Value, precision: 3);
    }

    [Fact]
    public void BytesArrivingAfterTheEstimateRanOut_BringTheBarBack()
    {
        var bar = new LoadProgressPresenter();

        var spent = bar.Update(At(elapsedMs: 12_000, read: 0, Share));
        Assert.True(spent.Indeterminate);

        var arriving = bar.Update(At(elapsedMs: 12_500, read: 10 * MB, Share));

        Assert.False(arriving.Indeterminate);
        Assert.Equal(10f / 11 * 0.1f, arriving.Value, precision: 3);
    }

    [Fact]
    public void OnceFollowingBytes_TheBarNeverRunsBackwards()
    {
        var bar = new LoadProgressPresenter();

        var first = bar.Update(At(elapsedMs: 2000, read: 40 * MB, source: null));

        var later = bar.Update(At(elapsedMs: 8000, read: 40 * MB, source: null));

        Assert.True(later.Value >= first.Value);
    }

    [Fact]
    public void AfterASlowTransfer_TheDecodeIsTimedFromWhenItEnded()
    {
        var bar = new LoadProgressPresenter();

        bar.Update(At(elapsedMs: 10_000, read: 50 * MB, Share));

        var arrived = bar.Update(At(elapsedMs: 20_000, read: 100 * MB, Share));
        var halfDecoded = bar.Update(At(elapsedMs: 20_500, read: 100 * MB, Share));

        Assert.Equal(10f / 11, arrived.Value, precision: 3);
        Assert.Equal(10f / 11 + 0.5f / 11, halfDecoded.Value, precision: 3);
    }

    [Fact]
    public void WithoutAMeasuredSource_TheDecodeShareIsFixedWhenTheTransferEnds()
    {
        var bar = new LoadProgressPresenter();

        var arrived = bar.Update(At(elapsedMs: 20_000, read: 100 * MB, source: null));
        var halfDecoded = bar.Update(At(elapsedMs: 20_500, read: 100 * MB, source: null));

        Assert.Equal(20f / 21, arrived.Value, precision: 3);
        Assert.Equal(20f / 21 + 0.5f / 21, halfDecoded.Value, precision: 3);
    }
}
