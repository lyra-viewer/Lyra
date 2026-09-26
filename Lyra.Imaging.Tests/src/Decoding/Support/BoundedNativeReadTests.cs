using System.Diagnostics;
using Lyra.Imaging.Decoding.Support;
using Lyra.Imaging.Loading;
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

        var ok = BoundedNativeRead.Attempt("fast read", ExpectedBytes,
            (out IntPtr pixels) =>
            {
                pixels = Buffer;
                return true;
            },
            out var returned, out var timedOut, freed.Add, ShortBound, TestContext.Current.CancellationToken);

        Assert.True(ok);
        Assert.False(timedOut);
        Assert.Equal(Buffer, returned);
        Assert.Empty(freed); // the caller owns it now and frees it itself
    }
    
    [Fact]
    public void ReadThatFails_IsNotReportedAsATimeout()
    {
        var ok = BoundedNativeRead.Attempt("failing read", ExpectedBytes,
            (out IntPtr pixels) =>
            {
                pixels = IntPtr.Zero;
                return false;
            },
            out var returned, out var timedOut, _ => { }, ShortBound, TestContext.Current.CancellationToken);

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
            var ok = BoundedNativeRead.Attempt("stalled read", ExpectedBytes,
                (out IntPtr pixels) =>
                {
                    pixels = IntPtr.Zero;
                    wedged.Wait();
                    return true;
                },
                out var returned, out var timedOut, _ => { }, ShortBound, TestContext.Current.CancellationToken);

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

        var ok = BoundedNativeRead.Attempt("late read", ExpectedBytes,
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
            }, ShortBound, TestContext.Current.CancellationToken);

        Assert.False(ok);
        Assert.True(timedOut);

        // Let the abandoned read finish; its buffer should be handed to the release callback.
        wedged.Set();

        Assert.True(freedLate.Wait(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken), "A late buffer should have been freed");
        Assert.Equal([Buffer], freed);
    }
    
    [Fact]
    public void CancelledRead_StopsWaitingAtOnce_AndStillFreesTheLateBuffer()
    {
        using var wedged = new ManualResetEventSlim(false);
        using var started = new ManualResetEventSlim(false);
        using var freedLate = new ManualResetEventSlim(false);
        using var cts = new CancellationTokenSource();

        var freed = new List<IntPtr>();
        var longBound = TimeSpan.FromMinutes(5);

        var sw = Stopwatch.StartNew();

        var run = Task.Run(() => BoundedNativeRead.Attempt("cancelled read", ExpectedBytes,
            (out IntPtr pixels) =>
            {
                pixels = Buffer;
                started.Set();
                wedged.Wait();
                return true;
            },
            out _, out _, late =>
            {
                freed.Add(late);
                freedLate.Set();
            }, longBound, cts.Token), TestContext.Current.CancellationToken);

        Assert.True(started.Wait(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken), "The read should have started");
        cts.Cancel();

        Assert.ThrowsAny<OperationCanceledException>(() => run.GetAwaiter().GetResult());
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(10), $"Cancellation should not wait out the bound, took {sw.Elapsed}");

        wedged.Set();

        Assert.True(freedLate.Wait(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken), "A late buffer should have been freed");
        Assert.Equal([Buffer], freed);
    }

    [Fact]
    public void AlreadyCancelled_NeverStartsTheRead()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var started = false;

        Assert.ThrowsAny<OperationCanceledException>(() => BoundedNativeRead.Attempt("pre-cancelled read", ExpectedBytes,
            (out IntPtr pixels) =>
            {
                started = true;
                pixels = IntPtr.Zero;
                return true;
            },
            out _, out _, _ => { }, ShortBound, cts.Token));

        Assert.False(started);
    }

    [Fact]
    public void ReadFromALowPriorityThread_RunsAtThatPriority()
    {
        ThreadPriority? readAt = null;
        Exception? failure = null;

        var caller = new Thread(() =>
        {
            try
            {
                BoundedNativeRead.Attempt("preload read", ExpectedBytes,
                    (out IntPtr pixels) =>
                    {
                        readAt = Thread.CurrentThread.Priority;
                        pixels = IntPtr.Zero;
                        return false;
                    },
                    out _, out _, _ => { }, ShortBound, TestContext.Current.CancellationToken);
            }
            catch (Exception ex)
            {
                failure = ex;
            }
        })
        {
            Priority = ThreadPriority.BelowNormal
        };

        caller.Start();
        Assert.True(caller.Join(TimeSpan.FromSeconds(10)), "The read should have finished");

        Assert.Null(failure);
        Assert.Equal(ThreadPriority.BelowNormal, readAt);
    }

    [Fact]
    public void ReadInPreloadWork_WaitsForTheForegroundBeforeStarting()
    {
        var busyUntil = Stopwatch.StartNew();
        long startedAt = -1;

        using (ForegroundYield.EnterLoad("neighbor.tif", () => busyUntil.ElapsedMilliseconds < 200, _ => { }))
        {
            BoundedNativeRead.Attempt("preload read", ExpectedBytes,
                (out IntPtr pixels) =>
                {
                    startedAt = busyUntil.ElapsedMilliseconds;
                    pixels = IntPtr.Zero;
                    return false;
                },
                out _, out _, _ => { }, ShortBound, TestContext.Current.CancellationToken);
        }

        Assert.True(startedAt >= 200, $"The read started at {startedAt} ms, while the foreground was still loading");
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
