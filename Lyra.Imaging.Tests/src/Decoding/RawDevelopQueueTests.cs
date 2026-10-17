using System.Collections.Concurrent;
using Lyra.Imaging.Decoding.Decoders.Raw;
using Xunit;

namespace Lyra.Imaging.Tests.Decoding;

public class RawDevelopQueueTests
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);

    /// <summary>Long enough for an idle worker to have picked up anything it was going to.</summary>
    private static readonly TimeSpan Settle = TimeSpan.FromMilliseconds(300);

    private static readonly Func<bool> Always = () => true;
    private static readonly Func<bool> Never = () => false;

    private static Func<bool> Signal(ManualResetEventSlim started) => () =>
    {
        started.Set();
        return true;
    };

    [Fact]
    public void Work_RunsOnTheDevelopThread_AndReturnsItsResult()
    {
        using var queue = new RawDevelopQueue();

        var thread = queue.Run(() => Thread.CurrentThread.Name, foreground: Always, TestContext.Current.CancellationToken);

        Assert.Equal("RawDevelop", thread);
    }

    [Fact]
    public void AFailure_ReachesTheCaller()
    {
        using var queue = new RawDevelopQueue();

        var error = Assert.Throws<InvalidOperationException>(() =>
            queue.Run<int>(() => throw new InvalidOperationException("broken raw"), foreground: Always, TestContext.Current.CancellationToken));

        Assert.Equal("broken raw", error.Message);
    }

    [Fact]
    public async Task WorkCancelledBeforeItStarts_NeverRuns()
    {
        using var queue = new RawDevelopQueue();
        using var release = new ManualResetEventSlim(false);
        using var cts = new CancellationTokenSource();

        // Hold the thread.
        var blockers = Enumerable.Range(0, 2)
            .Select(_ => Task.Run(() => queue.Run(() => release.Wait(Patience), foreground: Always, CancellationToken.None)))
            .ToArray();

        var ran = false;
        var waiting = Task.Run(() => queue.Run(() => ran = true, foreground: Always, cts.Token));

        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waiting);

        release.Set();
        await Task.WhenAll(blockers);

        // Queued after it, so by the time this has run the cancelled job has been passed over.
        queue.Run(() => 0, foreground: Always, TestContext.Current.CancellationToken);

        Assert.False(ran);
    }

    [Fact]
    public async Task StartedWork_IsWaitedFor_EvenWhenCancelled()
    {
        using var queue = new RawDevelopQueue();
        using var started = new ManualResetEventSlim(false);
        using var cts = new CancellationTokenSource();

        var finished = false;
        var run = Task.Run(() => queue.Run(() =>
        {
            started.Set();
            Thread.Sleep(200);
            finished = true;
            return 1;
        }, foreground: Always, cts.Token));

        Assert.True(started.Wait(Patience, TestContext.Current.CancellationToken));
        cts.Cancel();

        Assert.Equal(1, await run);
        Assert.True(finished);
    }

    [Fact]
    public async Task APreloadThatCannotBeCutShort_IsWaitedOut_ThenTheImageOnScreenGoesFirst()
    {
        using var queue = new RawDevelopQueue();
        using var release = new ManualResetEventSlim(false);
        using var preloadStarted = new ManualResetEventSlim(false);
        var order = new ConcurrentQueue<string>();

        var preload = Task.Run(() => queue.Run(() =>
        {
            preloadStarted.Set();
            release.Wait(Patience);
            order.Enqueue("preload");
            return 0;
        }, foreground: Never, CancellationToken.None));

        Assert.True(preloadStarted.Wait(Patience, TestContext.Current.CancellationToken));

        using var secondQueued = new ManualResetEventSlim(false);
        var secondPreload = Task.Run(() => queue.Run(() =>
        {
            order.Enqueue("second preload");
            return 0;
        }, foreground: () =>
        {
            secondQueued.Set();
            return false;
        }, CancellationToken.None));

        Assert.True(secondQueued.Wait(Patience, TestContext.Current.CancellationToken));

        using var onScreenQueued = new ManualResetEventSlim(false);
        var onScreen = Task.Run(() => queue.Run(() =>
        {
            order.Enqueue("on screen");
            return 0;
        }, foreground: Signal(onScreenQueued), CancellationToken.None));

        Assert.True(onScreenQueued.Wait(Patience, TestContext.Current.CancellationToken));
        await Task.Delay(Settle, TestContext.Current.CancellationToken);
        Assert.Empty(order);

        release.Set();
        await Task.WhenAll(preload, secondPreload, onScreen);
        Assert.Equal(new[] { "preload", "on screen", "second preload" }, order.ToArray());
    }

    [Fact]
    public async Task AQueuedPreload_WhoseImageComesOnScreen_CutsTheRunningPreloadShort()
    {
        using var queue = new RawDevelopQueue();
        using var first = new LongDevelop();
        using var secondStarted = new ManualResetEventSlim(false);

        var firstRun = Task.Run(() => queue.Run(first.Work, foreground: Never, CancellationToken.None));
        Assert.True(first.Started.Wait(Patience, TestContext.Current.CancellationToken));

        var onScreen = false;
        var second = Task.Run(() => queue.Run(Signal(secondStarted), foreground: () => Volatile.Read(ref onScreen), CancellationToken.None));

        Assert.False(secondStarted.Wait(Settle, TestContext.Current.CancellationToken));
        Assert.False(first.CutShort.IsSet);

        Volatile.Write(ref onScreen, true);
        queue.Reprioritize();

        Assert.True(secondStarted.Wait(Patience, TestContext.Current.CancellationToken));
        Assert.True(first.CutShort.IsSet);

        await Task.WhenAll(firstRun, second);
        Assert.Equal(2, first.Runs);
    }

    [Fact]
    public async Task APreload_WaitsWhileTheImageOnScreenDevelops()
    {
        using var queue = new RawDevelopQueue();
        using var release = new ManualResetEventSlim(false);
        using var onScreenStarted = new ManualResetEventSlim(false);
        using var preloadStarted = new ManualResetEventSlim(false);

        var onScreen = Task.Run(() => queue.Run(() =>
        {
            onScreenStarted.Set();
            return release.Wait(Patience);
        }, foreground: Always, CancellationToken.None));

        Assert.True(onScreenStarted.Wait(Patience, TestContext.Current.CancellationToken));

        var preload = Task.Run(() => queue.Run(Signal(preloadStarted), foreground: Never, CancellationToken.None));

        Assert.False(preloadStarted.Wait(Settle, TestContext.Current.CancellationToken));

        release.Set();

        Assert.True(preloadStarted.Wait(Patience, TestContext.Current.CancellationToken));
        await Task.WhenAll(onScreen, preload);
    }

    [Fact]
    public async Task APreload_StartsOnceTheAnnouncedImageLeavesTheScreen()
    {
        using var queue = new RawDevelopQueue();
        using var preloadStarted = new ManualResetEventSlim(false);

        var onScreen = true;
        Func<bool> isOnScreen = () => Volatile.Read(ref onScreen);
        using var announced = queue.Announce(isOnScreen);
        Assert.Equal(1, queue.Run(() => 1, foreground: isOnScreen, TestContext.Current.CancellationToken));

        var preload = Task.Run(() => queue.Run(Signal(preloadStarted), foreground: Never, CancellationToken.None));
        Assert.False(preloadStarted.Wait(Settle, TestContext.Current.CancellationToken));

        Volatile.Write(ref onScreen, false);
        queue.Reprioritize();

        Assert.True(preloadStarted.Wait(Patience, TestContext.Current.CancellationToken));
        await preload;
    }

    [Fact]
    public async Task APreload_WaitsWhileADevelopForTheImageOnScreenIsAnnounced()
    {
        using var queue = new RawDevelopQueue();
        using var preloadStarted = new ManualResetEventSlim(false);

        var announced = queue.Announce(Always);

        var preload = Task.Run(() => queue.Run(Signal(preloadStarted), foreground: Never, CancellationToken.None));
        Assert.False(preloadStarted.Wait(Settle, TestContext.Current.CancellationToken));

        // Work for the image on screen itself is never held back.
        Assert.Equal(7, queue.Run(() => 7, foreground: Always, TestContext.Current.CancellationToken));
        Assert.False(preloadStarted.IsSet);

        announced.Dispose();

        Assert.True(preloadStarted.Wait(Patience, TestContext.Current.CancellationToken));
        await preload;
    }

    [Fact]
    public void AnAnnouncementForAnImageOffScreen_HoldsNothingBack()
    {
        using var queue = new RawDevelopQueue();
        using var announced = queue.Announce(Never);

        Assert.Equal(3, queue.Run(() => 3, foreground: Never, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task AHeldPreload_CanStillBeCancelled()
    {
        using var queue = new RawDevelopQueue();
        using var announced = queue.Announce(Always);
        using var cts = new CancellationTokenSource();

        var ran = false;
        var preload = Task.Run(() => queue.Run(() => ran = true, foreground: Never, cts.Token));

        await Task.Delay(Settle, TestContext.Current.CancellationToken);
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => preload);
        Assert.False(ran);
    }

    [Fact]
    public void AForegroundCheckThatThrows_CountsAsBackground_AndKeepsTheWorkerAlive()
    {
        using var queue = new RawDevelopQueue();

        Assert.Equal(1, queue.Run(() => 1, foreground: () => throw new InvalidOperationException(), TestContext.Current.CancellationToken));
        Assert.Equal(2, queue.Run(() => 2, foreground: Always, TestContext.Current.CancellationToken));
    }

    /// <summary>Work that runs until cut short or released, as a long LibRaw develop would; only its first run is long.</summary>
    private sealed class LongDevelop : IDisposable
    {
        public readonly ManualResetEventSlim Started = new(false);
        public readonly ManualResetEventSlim CutShort = new(false);
        public readonly ManualResetEventSlim Release = new(false);
        private int _runs;
        private int _active;

        public int Runs => Volatile.Read(ref _runs);

        public bool Active => Volatile.Read(ref _active) > 0;

        public Action? OnRun { get; init; }

        public int Work(CancellationToken preempt)
        {
            var run = Interlocked.Increment(ref _runs);
            Interlocked.Increment(ref _active);

            try
            {
                OnRun?.Invoke();
                Started.Set();

                if (run == 1)
                {
                    WaitHandle.WaitAny([preempt.WaitHandle, Release.WaitHandle], Patience);

                    if (preempt.IsCancellationRequested && !Release.IsSet)
                    {
                        CutShort.Set();
                        preempt.ThrowIfCancellationRequested();
                    }
                }

                return run;
            }
            finally
            {
                Interlocked.Decrement(ref _active);
            }
        }

        public void Dispose()
        {
            Started.Dispose();
            CutShort.Dispose();
            Release.Dispose();
        }
    }

    [Fact]
    public async Task ARunningPreload_IsCutShortForTheImageOnScreen_AndRunsAgainAfterIt()
    {
        using var queue = new RawDevelopQueue();
        using var preload = new LongDevelop();

        var background = Task.Run(() => queue.Run(preload.Work, foreground: Never, CancellationToken.None));
        Assert.True(preload.Started.Wait(Patience, TestContext.Current.CancellationToken));

        var preloadActiveAtStart = true;
        var runsWhileOnScreenDeveloped = queue.Run(() =>
        {
            preloadActiveAtStart = preload.Active;
            Thread.Sleep(Settle);
            return preload.Runs;
        }, foreground: Always, TestContext.Current.CancellationToken);

        Assert.True(preload.CutShort.IsSet);
        Assert.False(preloadActiveAtStart);
        Assert.Equal(1, runsWhileOnScreenDeveloped);
        Assert.Equal(2, await background);
    }

    [Fact]
    public async Task ARunningPreload_IsCutShortByAnAnnouncement_AndWaitsItOut()
    {
        using var queue = new RawDevelopQueue();
        using var preload = new LongDevelop();

        var background = Task.Run(() => queue.Run(preload.Work, foreground: Never, CancellationToken.None));
        Assert.True(preload.Started.Wait(Patience, TestContext.Current.CancellationToken));

        var announced = queue.Announce(Always);
        Assert.True(preload.CutShort.Wait(Patience, TestContext.Current.CancellationToken));

        await Task.Delay(Settle, TestContext.Current.CancellationToken);
        Assert.Equal(1, preload.Runs);

        announced.Dispose();
        Assert.Equal(2, await background);
    }

    [Fact]
    public async Task ARunningPreload_IsCutShortOnceTheAnnouncedImageComesOnScreen()
    {
        using var queue = new RawDevelopQueue();
        using var preload = new LongDevelop();

        var background = Task.Run(() => queue.Run(preload.Work, foreground: Never, CancellationToken.None));
        Assert.True(preload.Started.Wait(Patience, TestContext.Current.CancellationToken));

        var onScreen = false;
        using var announced = queue.Announce(() => Volatile.Read(ref onScreen));
        Assert.False(preload.CutShort.Wait(Settle, TestContext.Current.CancellationToken));

        Volatile.Write(ref onScreen, true);
        queue.Reprioritize();
        Assert.True(preload.CutShort.Wait(Patience, TestContext.Current.CancellationToken));

        Volatile.Write(ref onScreen, false);
        queue.Reprioritize();
        Assert.Equal(2, await background);
    }

    [Fact]
    public async Task WorkForTheImageOnScreen_IsNeverCutShort()
    {
        using var queue = new RawDevelopQueue();
        using var onScreen = new LongDevelop();

        var developing = Task.Run(() => queue.Run(onScreen.Work, foreground: Always, CancellationToken.None));
        Assert.True(onScreen.Started.Wait(Patience, TestContext.Current.CancellationToken));

        using var announced = queue.Announce(Always);
        var more = Task.Run(() => queue.Run(() => 5, foreground: Always, CancellationToken.None));
        queue.Reprioritize();

        Assert.False(onScreen.CutShort.Wait(Settle, TestContext.Current.CancellationToken));

        onScreen.Release.Set();
        Assert.Equal(1, await developing);
        Assert.Equal(5, await more);
    }

    [Fact]
    public async Task APreloadWhoseImageComesOnScreen_IsNotCutShortByItsOwnAnnouncement()
    {
        using var queue = new RawDevelopQueue();
        using var preload = new LongDevelop();

        var current = false;
        Func<bool> isCurrent = () => Volatile.Read(ref current);
        using var announced = queue.Announce(isCurrent);

        var developing = Task.Run(() => queue.Run(preload.Work, foreground: isCurrent, CancellationToken.None));
        Assert.True(preload.Started.Wait(Patience, TestContext.Current.CancellationToken));

        Volatile.Write(ref current, true);
        queue.Reprioritize();
        Assert.False(preload.CutShort.Wait(Settle, TestContext.Current.CancellationToken));

        preload.Release.Set();
        Assert.Equal(1, await developing);
    }

    [Fact]
    public async Task ACutShortPreload_GoesBehindWorkNotCutShort_EvenWorkQueuedAfterIt()
    {
        using var queue = new RawDevelopQueue();
        var order = new ConcurrentQueue<string>();
        using var first = new LongDevelop { OnRun = () => order.Enqueue("first") };

        var firstRun = Task.Run(() => queue.Run(first.Work, foreground: Never, CancellationToken.None));
        Assert.True(first.Started.Wait(Patience, TestContext.Current.CancellationToken));

        // The queue weighs waiting work while a preload runs, so a call means the second one is in.
        using var secondQueued = new ManualResetEventSlim(false);
        var secondRun = Task.Run(() => queue.Run(() =>
        {
            order.Enqueue("second");
            return 0;
        }, foreground: () =>
        {
            secondQueued.Set();
            return false;
        }, CancellationToken.None));

        Assert.True(secondQueued.Wait(Patience, TestContext.Current.CancellationToken));
        var announced = queue.Announce(Always);
        Assert.True(first.CutShort.Wait(Patience, TestContext.Current.CancellationToken));

        using var thirdQueued = new ManualResetEventSlim(false);
        var thirdRun = Task.Run(() => queue.Run(() =>
        {
            order.Enqueue("third");
            return 0;
        }, foreground: () =>
        {
            thirdQueued.Set();
            return false;
        }, CancellationToken.None));

        Assert.True(thirdQueued.Wait(Patience, TestContext.Current.CancellationToken));
        announced.Dispose();

        await Task.WhenAll(firstRun, secondRun, thirdRun);
        Assert.Equal(new[] { "first", "second", "third", "first" }, order.ToArray());
    }

    [Fact]
    public async Task ACutShortPreload_WhoseLoadWasCancelled_DoesNotRunAgain()
    {
        using var queue = new RawDevelopQueue();
        using var preload = new LongDevelop();
        using var cts = new CancellationTokenSource();

        var background = Task.Run(() => queue.Run(preload.Work, foreground: Never, cts.Token));
        Assert.True(preload.Started.Wait(Patience, TestContext.Current.CancellationToken));

        cts.Cancel();
        using var announced = queue.Announce(Always);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => background);
        Assert.Equal(1, preload.Runs);
    }

    [Fact]
    public async Task ARunningPreload_ThatIsFinishing_IsWaitedOut_ThenTheImageOnScreenGoesFirst()
    {
        using var queue = new RawDevelopQueue();
        using var preload = new LongDevelop();

        var background = Task.Run(() => queue.Run(preload.Work, foreground: Never, finishing: Always, CancellationToken.None));
        Assert.True(preload.Started.Wait(Patience, TestContext.Current.CancellationToken));

        var preloadActiveAtStart = true;
        var onScreen = Task.Run(() => queue.Run(() => preloadActiveAtStart = preload.Active, foreground: Always, CancellationToken.None));
        var more = Task.Run(() => queue.Run(() => 3, foreground: Never, CancellationToken.None));

        Assert.False(preload.CutShort.Wait(Settle, TestContext.Current.CancellationToken));
        Assert.False(onScreen.IsCompleted);

        preload.Release.Set();
        Assert.Equal(1, await background);
        Assert.False(await onScreen);
        Assert.Equal(3, await more);
        Assert.Equal(1, preload.Runs);
    }

    [Fact]
    public async Task ARunningPreload_IsAskedWhetherItIsFinishing_WhenTheImageOnScreenArrives()
    {
        using var queue = new RawDevelopQueue();
        using var preload = new LongDevelop();

        var finishing = false;
        var background = Task.Run(() => queue.Run(preload.Work, foreground: Never, finishing: () => Volatile.Read(ref finishing), CancellationToken.None));
        Assert.True(preload.Started.Wait(Patience, TestContext.Current.CancellationToken));

        Volatile.Write(ref finishing, true);
        using (queue.Announce(Always))
            Assert.False(preload.CutShort.Wait(Settle, TestContext.Current.CancellationToken));

        preload.Release.Set();
        Assert.Equal(1, await background);
    }

    [Fact]
    public async Task AFinishingCheckThatThrows_CountsAsNotFinishing()
    {
        using var queue = new RawDevelopQueue();
        using var preload = new LongDevelop();

        var background = Task.Run(() => queue.Run(preload.Work, foreground: Never, finishing: () => throw new ObjectDisposedException("image"), CancellationToken.None));
        Assert.True(preload.Started.Wait(Patience, TestContext.Current.CancellationToken));

        using (queue.Announce(Always))
            Assert.True(preload.CutShort.Wait(Patience, TestContext.Current.CancellationToken));

        Assert.Equal(2, await background);
    }

    [Fact]
    public async Task RunAsync_HoldsNoThreadWhileTheWorkWaits()
    {
        using var queue = new RawDevelopQueue();
        using var started = new ManualResetEventSlim(false);
        using var release = new ManualResetEventSlim(false);

        var blocker = Task.Run(() => queue.Run(() =>
        {
            started.Set();
            return release.Wait(Patience);
        }, foreground: Always, CancellationToken.None));
        Assert.True(started.Wait(Patience, TestContext.Current.CancellationToken));

        var waiting = queue.RunAsync(_ => 4, foreground: Never, finishing: Never, CancellationToken.None);
        await Task.Delay(Settle, TestContext.Current.CancellationToken);
        Assert.False(waiting.IsCompleted);

        release.Set();
        Assert.Equal(4, await waiting);
        Assert.True(await blocker);
    }

    [Fact]
    public async Task RunAsync_NeverContinuesOnTheDevelopThread()
    {
        using var queue = new RawDevelopQueue();

        var continuedOn = await queue.RunAsync(_ => 0, foreground: Always, finishing: Never, CancellationToken.None)
            .ContinueWith(_ => Thread.CurrentThread.Name, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);

        Assert.NotEqual("RawDevelop", continuedOn);
    }

    [Fact]
    public async Task RunAsync_CancelledBeforeItStarts_EndsCancelled_AndNeverRuns()
    {
        using var queue = new RawDevelopQueue();
        using var started = new ManualResetEventSlim(false);
        using var release = new ManualResetEventSlim(false);
        using var cts = new CancellationTokenSource();

        var blocker = Task.Run(() => queue.Run(() =>
        {
            started.Set();
            return release.Wait(Patience);
        }, foreground: Always, CancellationToken.None));
        Assert.True(started.Wait(Patience, TestContext.Current.CancellationToken));

        var ran = false;
        var waiting = queue.RunAsync(_ => ran = true, foreground: Always, finishing: Never, cts.Token);

        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waiting);
        Assert.True(waiting.IsCanceled);

        release.Set();
        await blocker;
        queue.Run(() => 0, foreground: Always, TestContext.Current.CancellationToken);
        Assert.False(ran);
    }

    [Fact]
    public async Task RunAsync_OnAStoppedQueue_Fails()
    {
        var queue = new RawDevelopQueue();
        queue.Dispose();

        await Assert.ThrowsAsync<ObjectDisposedException>(() => queue.RunAsync(_ => 0, foreground: Always, finishing: Never, CancellationToken.None));
    }

    [Fact]
    public async Task WorkThatFinishesDespiteTheCut_KeepsItsResult()
    {
        using var queue = new RawDevelopQueue();
        using var started = new ManualResetEventSlim(false);
        var runs = 0;

        var background = Task.Run(() => queue.Run(preempt =>
        {
            Interlocked.Increment(ref runs);
            started.Set();
            preempt.WaitHandle.WaitOne(Patience);
            return 9;
        }, foreground: Never, CancellationToken.None));

        Assert.True(started.Wait(Patience, TestContext.Current.CancellationToken));
        using (queue.Announce(Always))
            Assert.Equal(9, await background);

        Assert.Equal(1, runs);
    }
}