using System.Diagnostics;
using Lyra.Imaging.Decoding.Support;
using Xunit;

namespace Lyra.Imaging.Tests.Decoding.Support;

public class BoundedNativeReadTests
{
    private static readonly TimeSpan ShortBound = TimeSpan.FromMilliseconds(250);

    private static readonly IntPtr Buffer = 0x1234;
    
    private const long ExpectedBytes = 16L * 1024 * 1024;

    [Fact]
    public void ReadThatReturnsInTime_IsReportedAsItself()
    {
        var freed = new List<IntPtr>();

        var ok = BoundedNativeRead.Run("fast read", ExpectedBytes,
            (out IntPtr pixels) =>
            {
                pixels = Buffer;
                return true;
            },
            out var returned, out var timedOut, freed.Add, ShortBound);

        Assert.True(ok);
        Assert.False(timedOut);
        Assert.Equal(Buffer, returned);
        Assert.Empty(freed); // the caller owns it now and frees it itself
    }
    
    [Fact]
    public void ReadThatFails_IsNotReportedAsATimeout()
    {
        var ok = BoundedNativeRead.Run("failing read", ExpectedBytes,
            (out IntPtr pixels) =>
            {
                pixels = IntPtr.Zero;
                return false;
            },
            out var returned, out var timedOut, _ => { }, ShortBound);

        Assert.False(ok);
        Assert.False(timedOut);
        Assert.Equal(IntPtr.Zero, returned);
    }

    [Fact]
    public void StalledRead_GivesUpOnTheBound_RatherThanWaitingForTheRead()
    {
        using var wedged = new ManualResetEventSlim(false);

        var sw = Stopwatch.StartNew();

        try
        {
            var ok = BoundedNativeRead.Run("stalled read", ExpectedBytes,
                (out IntPtr pixels) =>
                {
                    pixels = IntPtr.Zero;
                    wedged.Wait();
                    return true;
                },
                out var returned, out var timedOut, _ => { }, ShortBound);

            sw.Stop();

            Assert.False(ok);
            Assert.True(timedOut);
            Assert.Equal(IntPtr.Zero, returned);
            Assert.True(sw.Elapsed < ShortBound + TimeSpan.FromSeconds(5), $"Should have given up at about the bound, took {sw.Elapsed}");
        }
        finally
        {
            wedged.Set();
        }
    }
    
    [Fact]
    public void BufferFromAnAbandonedRead_IsFreedWhenItArrivesLate()
    {
        using var wedged = new ManualResetEventSlim(false);
        using var freedLate = new ManualResetEventSlim(false);

        var freed = new List<IntPtr>();

        var ok = BoundedNativeRead.Run("late read", ExpectedBytes,
            (out IntPtr pixels) =>
            {
                pixels = Buffer;
                wedged.Wait();
                return true;
            },
            out _, out var timedOut, late =>
            {
                freed.Add(late);
                freedLate.Set();
            }, ShortBound);

        Assert.False(ok);
        Assert.True(timedOut);

        // Let the abandoned read finish; its buffer should be handed to the release callback.
        wedged.Set();

        Assert.True(freedLate.Wait(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken), "A late buffer should have been freed");
        Assert.Equal([Buffer], freed);
    }
    
    [Fact]
    public void Bound_ClearsWhatARealBandTakesOnASlowShare()
    {
        var band = BoundedNativeRead.BoundFor(200L * 1024 * 1024);

        Assert.True(band > TimeSpan.FromMinutes(3), $"A 200 MB band gets {band.TotalSeconds:F0}s, too close to the ~50s it can legitimately take");
    }

    [Fact]
    public void Bound_HasAFloorForSmallReads()
    {
        Assert.Equal(BoundedNativeRead.BoundFor(0), BoundedNativeRead.BoundFor(1024));
        Assert.True(BoundedNativeRead.BoundFor(1024) >= TimeSpan.FromSeconds(60));
    }

    [Fact]
    public void Bound_IsCappedForHugeReads()
    {
        Assert.Equal(BoundedNativeRead.BoundFor(long.MaxValue / 2), BoundedNativeRead.BoundFor(100L * 1024 * 1024 * 1024));
        Assert.True(BoundedNativeRead.BoundFor(long.MaxValue / 2) <= TimeSpan.FromMinutes(10));
    }
}
