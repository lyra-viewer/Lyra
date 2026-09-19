using Lyra.Common.Estimation;
using Xunit;

namespace Lyra.Imaging.Tests.Loading;

public class SourceThroughputTests
{
    private const long Mb = 1024 * 1024;

    private static string OnSource(string name) => Path.Combine(Path.GetTempPath(), name);

    [Fact]
    public void NothingIsClaimedBeforeThereAreEnoughReads()
    {
        var samples = new SourceThroughputSamples();

        Assert.Null(samples.Estimate(OnSource("a.jpg")));

        samples.Record(OnSource("a.jpg"), 4 * Mb, 40);
        samples.Record(OnSource("b.jpg"), 4 * Mb, 40);

        Assert.Null(samples.Estimate(OnSource("c.jpg")));
    }
    
    [Fact]
    public void UniformFileSizesStillPredictThatSizeCorrectly()
    {
        var samples = new SourceThroughputSamples();

        for (var i = 0; i < 6; i++)
            samples.Record(OnSource($"same{i}.jpg"), 4 * Mb, 100);

        var estimate = Assert.IsType<TransferEstimate>(samples.Estimate(OnSource("next.jpg")));

        Assert.Equal(100, estimate.MsFor(4 * Mb), precision: 6);
    }
    
    [Fact]
    public void AFixedPerFileCostIsSeparatedFromTheRate()
    {
        var samples = new SourceThroughputSamples();

        // A source with 50 ms of latency and 100 bytes/ms... in units: 100 MB/s = 104857.6 B/ms.
        const double bytesPerMs = 100 * 1024 * 1024 / 1000.0;
        const double latency = 50;

        var sizes = new[] { 256L * 1024, 512 * 1024, Mb, 8 * Mb, 32 * Mb, 64 * Mb };
        for (var i = 0; i < sizes.Length; i++)
            samples.Record(OnSource($"spread{i}.bin"), sizes[i], latency + sizes[i] / bytesPerMs);

        var estimate = Assert.IsType<TransferEstimate>(samples.Estimate(OnSource("next.bin")));

        Assert.Equal(latency, estimate.LatencyMs, tolerance: 5);
        Assert.Equal(bytesPerMs, estimate.BytesPerMs, tolerance: bytesPerMs * 0.1);

        // The point of the two terms: a small file is mostly round trip, not bytes.
        Assert.Equal(latency + 256 * 1024 / bytesPerMs, estimate.MsFor(256 * 1024), tolerance: 6);
    }
    
    [Fact]
    public void ReadsAtCacheSpeedAreNotBelieved()
    {
        var samples = new SourceThroughputSamples();

        for (var i = 0; i < 6; i++)
            samples.Record(OnSource($"slow{i}.bin"), 10 * Mb, 100); // ~100 MB/s

        // 10 MB in a fraction of a millisecond is memory, not storage.
        for (var i = 0; i < 6; i++)
            samples.Record(OnSource($"cached{i}.bin"), 10 * Mb, 0.5);

        var estimate = Assert.IsType<TransferEstimate>(samples.Estimate(OnSource("next.bin")));

        Assert.Equal(100, estimate.MsFor(10 * Mb), tolerance: 15);
    }
    
    [Fact]
    public void ACachedReadUnderTheAbsoluteCeilingIsStillNotBelieved()
    {
        var samples = new SourceThroughputSamples(_ => "nfs");

        // ~11 MB/s, the measured truth of the mount, learned from a handful of cold reads.
        const double realBytesPerMs = 11.0 * Mb / 1000.0;
        var sizes = new[] { 16L * Mb, 64 * Mb, 200 * Mb };

        for (var i = 0; i < sizes.Length; i++)
            samples.Record($"cold{i}.bin", sizes[i], sizes[i] / realBytesPerMs);

        var before = Assert.IsType<TransferEstimate>(samples.Estimate("x.bin")).MsFor(200 * Mb);
        Assert.InRange(before, 15_000, 22_000); // ~18 s for 200 MB

        // Then a folder's worth of files the cache already holds, each 200 MB in 74 ms = 2.7 GB/s.
        // These OUTNUMBER the honest reads, which is the ordinary case when re-browsing a folder,
        // and is where a median over the raw samples stops protecting anything.
        for (var i = 0; i < 10; i++)
            samples.Record($"warm{i}.bin", 200 * Mb, 74);

        var after = Assert.IsType<TransferEstimate>(samples.Estimate("x.bin")).MsFor(200 * Mb);

        Assert.Equal(before, after, precision: 6);

        // The estimate must still describe the mount - ~18 s for 200 MB, not the cache's 74 ms.
        Assert.InRange(after, 15_000, 22_000);
    }
    
    [Fact]
    public void ASustainedSpeedUpIsEventuallyBelievedRatherThanRejectedForever()
    {
        var samples = new SourceThroughputSamples(_ => "mount");

        // Learned while the link was congested: ~2 MB/s.
        for (var i = 0; i < 4; i++)
            samples.Record($"slow{i}.bin", 10 * Mb, 5000);

        var congested = Assert.IsType<TransferEstimate>(samples.Estimate("x.bin")).MsFor(10 * Mb);
        Assert.InRange(congested, 4000, 6000);

        // The same mount, now on a fast link: ~100 MB/s, every read far beyond what the history
        // allows. Sustained, not a stray cache hit.
        for (var i = 0; i < 40; i++)
            samples.Record($"fast{i}.bin", 10 * Mb, 100);

        var recovered = Assert.IsType<TransferEstimate>(samples.Estimate("x.bin")).MsFor(10 * Mb);

        Assert.True(recovered < congested / 2, $"a sustained speed-up should eventually be learned ({congested:F0} ms -> {recovered:F0} ms)");
    }

    [Fact]
    public void AGenuinelyQuickerReadIsStillBelieved()
    {
        var samples = new SourceThroughputSamples(_ => "nfs");

        for (var i = 0; i < 6; i++)
            samples.Record($"steady{i}.bin", 10 * Mb, 1000); // ~10 MB/s

        var before = Assert.IsType<TransferEstimate>(samples.Estimate("x.bin")).MsFor(10 * Mb);

        // Three times quicker - plausible on a link that just got quiet, and inside the factor.
        for (var i = 0; i < 6; i++)
            samples.Record($"quick{i}.bin", 10 * Mb, 330);

        var after = Assert.IsType<TransferEstimate>(samples.Estimate("x.bin")).MsFor(10 * Mb);

        Assert.True(after < before, $"a real speed-up should move the estimate ({before:F0} ms -> {after:F0} ms)");
    }
    
    [Fact]
    public void WithNoHistoryTheAbsoluteCeilingStillApplies()
    {
        var samples = new SourceThroughputSamples(_ => "src");

        for (var i = 0; i < 6; i++)
            samples.Record($"ram{i}.bin", 10 * Mb, 0.5); // ~20 GB/s

        Assert.Null(samples.Estimate("next.bin"));
    }
    
    [Fact]
    public void AHistorySeededWithCacheReadsRecovers()
    {
        var samples = new SourceThroughputSamples(_ => "nfs");

        // A cold start that happens to catch warm files first: ~2.7 GB/s, under the ceiling.
        for (var i = 0; i < 4; i++)
            samples.Record($"warm{i}.bin", 200 * Mb, 74);

        // Then the truth arrives, file after file, at ~11 MB/s.
        for (var i = 0; i < 20; i++)
            samples.Record($"cold{i}.bin", 200 * Mb, 18_000);

        var estimate = Assert.IsType<TransferEstimate>(samples.Estimate("next.bin"));

        Assert.InRange(estimate.MsFor(200 * Mb), 14_000, 22_000);
    }

    [Fact]
    public void OnlyTheFirstReadOfAFileIsSampled()
    {
        var samples = new SourceThroughputSamples();

        for (var i = 0; i < 4; i++)
            samples.Record(OnSource($"once{i}.bin"), 10 * Mb, 100);

        var before = Assert.IsType<TransferEstimate>(samples.Estimate(OnSource("x.bin"))).MsFor(10 * Mb);

        // The same four files again, now warm - and still just under the implausible-rate ceiling,
        // so only the repeat-file rule can reject them.
        for (var i = 0; i < 4; i++)
            samples.Record(OnSource($"once{i}.bin"), 10 * Mb, 2);

        var after = Assert.IsType<TransferEstimate>(samples.Estimate(OnSource("x.bin"))).MsFor(10 * Mb);

        Assert.Equal(before, after, precision: 6);
    }
    
    [Fact]
    public void OneStalledReadDoesNotMoveTheEstimate()
    {
        var samples = new SourceThroughputSamples();

        for (var i = 0; i < 9; i++)
            samples.Record(OnSource($"steady{i}.bin"), 10 * Mb, 100);

        samples.Record(OnSource("stalled.bin"), 10 * Mb, 30_000);

        var estimate = Assert.IsType<TransferEstimate>(samples.Estimate(OnSource("next.bin")));

        Assert.Equal(100, estimate.MsFor(10 * Mb), tolerance: 15);
    }
    
    [Fact]
    public void AFittedRateCannotExceedWhatAnyDeviceCouldDo()
    {
        var samples = new SourceThroughputSamples(_ => "src");

        // Each read is plausible on its own - a slow start then a fast stream - but the line
        // through them barely rises with size, implying a rate no device sustains.
        samples.Record("a.bin", Mb, 100);
        samples.Record("b.bin", 100 * Mb, 105);
        samples.Record("c.bin", 200 * Mb, 110);
        samples.Record("d.bin", 400 * Mb, 120);

        var estimate = Assert.IsType<TransferEstimate>(samples.Estimate("e.bin"));

        Assert.True(estimate.BytesPerMs <= 8.0 * 1024 * 1024, $"fitted {estimate.BytesPerMs / 1024 / 1024:F1} GB/s exceeds the ceiling");
    }

    [Theory]
    [InlineData(0, 100)]
    [InlineData(-1, 100)]
    [InlineData(4 * 1024 * 1024, 0)]
    [InlineData(4 * 1024 * 1024, -1)]
    public void ImpossibleSamplesAreIgnored(long bytes, double ms)
    {
        var samples = new SourceThroughputSamples();

        for (var i = 0; i < 6; i++)
            samples.Record(OnSource($"junk{i}.bin"), bytes, ms);

        Assert.Null(samples.Estimate(OnSource("next.bin")));
    }
    
    [Fact]
    public void SourcesAreKeptApartRatherThanAveragedTogether()
    {
        // First path segment stands in for the mount.
        var samples = new SourceThroughputSamples(path => path.Split('/', '\\')[0]);

        for (var i = 0; i < 6; i++)
            samples.Record($"ssd/local{i}.bin", 10 * Mb, 10);   // ~1 GB/s
        
        for (var i = 0; i < 6; i++)
            samples.Record($"nas/remote{i}.bin", 10 * Mb, 500); // ~20 MB/s

        Assert.Equal(10, Assert.IsType<TransferEstimate>(samples.Estimate("ssd/next.bin")).MsFor(10 * Mb), tolerance: 2);
        Assert.Equal(500, Assert.IsType<TransferEstimate>(samples.Estimate("nas/next.bin")).MsFor(10 * Mb), tolerance: 50);

        // And a mount nothing has been read from claims nothing at all.
        Assert.Null(samples.Estimate("usb/other.bin"));
    }

    [Fact]
    public void PathsOnTheSameMountShareAKey()
    {
        var a = StorageSource.RootFor(Path.Combine(Path.GetTempPath(), "one", "a.jpg"));
        var b = StorageSource.RootFor(Path.Combine(Path.GetTempPath(), "two", "b.jpg"));

        Assert.Equal(a, b);
        Assert.NotEmpty(a);
    }
}
