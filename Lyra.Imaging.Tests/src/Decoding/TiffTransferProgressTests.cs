using Lyra.Imaging.Content;
using Lyra.Imaging.Decoding.Decoders.Tiff;
using Lyra.Imaging.Interop;
using Lyra.Imaging.Tests.Support;
using Xunit;

namespace Lyra.Imaging.Tests.Decoding;

/// <summary>
/// A streaming pass over a large TIFF reports how much it has read as it goes, not only once at
/// the end - which, over a slow share, is minutes of a progress bar with nothing behind it.
/// </summary>
public class TiffTransferProgressTests
{
    [Fact]
    public void EachReadIsVisibleBeforeThePassEnds()
    {
        using var file = new TempFile([0]);
        using var composite = new Composite(new FileInfo(file.Path));
        composite.BeginLoadTiming();

        var tally = new IoTally(composite);

        tally.Add(1_000_000, 90);
        Assert.Equal(1_000_000, composite.Timing.TransferBytesRead);
        Assert.False(composite.Timing.TransferMeasured); // still under way

        tally.Add(500_000, 45);
        Assert.Equal(1_500_000, composite.Timing.TransferBytesRead);
    }

    [Fact]
    public void ThePassIsCountedOnceWhenItEnds()
    {
        using var file = new TempFile([0]);
        using var composite = new Composite(new FileInfo(file.Path));
        composite.BeginLoadTiming();

        var tally = new IoTally(composite);

        tally.Add(1_000_000, 90);
        tally.Add(500_000, 45);
        tally.ReportTo(composite);

        Assert.True(composite.Timing.TransferMeasured);
        Assert.Equal(1_500_000, composite.Timing.TransferBytesRead);
        Assert.Equal(135, composite.Timing.TransferMs!.Value, precision: 3);
    }

    [Fact]
    public unsafe void TheNativeCounter_SeesEveryByteTheReadMoves_AndStopsWhenCleared()
    {
        Assert.SkipUnless(NativeWrapper.TryLoad("libtiff_native", typeof(TiffNative).Assembly,
                () => TiffNative.DescribeDirectories("lyra-absent.tif", IntPtr.Zero, 0)),
            "libtiff_native not available (native wrappers not built for this platform)."
        );

        using var file = new TempFile(TiffBuilder.Build(TiffBuilder.Gray(2048, 2048)));

        long counter = 0;

        Assert.True(TiffNative.SetIoProgress((IntPtr)(&counter)), "This build should have the progress entry point");

        try
        {
            Assert.True(TiffNative.LoadRegion(file.Path, 0, false, 0, 0, 2048, 2048, out var pixels, out _));
            TiffNative.free_tiff_pixels(pixels);
        }
        finally
        {
            TiffNative.SetIoProgress(IntPtr.Zero);
        }

        var counted = Interlocked.Read(ref counter);

        Assert.True(counted > 0);
        Assert.Equal(TiffNative.LastIo()!.Value.Bytes, counted);

        // Cleared: a further read on this thread is no longer counted here.
        Assert.True(TiffNative.LoadRegion(file.Path, 0, false, 0, 0, 16, 16, out var more, out _));
        TiffNative.free_tiff_pixels(more);

        Assert.Equal(counted, Interlocked.Read(ref counter));
    }

    [Fact]
    public void WithNothingToTell_ItStillAddsUp()
    {
        var tally = new IoTally();

        tally.Add(1_000, 1);

        using var file = new TempFile([0]);
        using var composite = new Composite(new FileInfo(file.Path));
        composite.BeginLoadTiming();

        tally.ReportTo(composite);

        Assert.Equal(1_000, composite.Timing.TransferBytesRead);
    }
}
