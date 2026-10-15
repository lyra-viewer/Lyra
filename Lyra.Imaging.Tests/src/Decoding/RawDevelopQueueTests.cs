using Lyra.Imaging.Decoding.Decoders.Raw;
using Xunit;

namespace Lyra.Imaging.Tests.Decoding;

public class RawDevelopQueueTests
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);

    [Fact]
    public void Work_RunsOnADevelopThread_AndReturnsItsResult()
    {
        using var queue = new RawDevelopQueue();

        var thread = queue.Run(() => Thread.CurrentThread.Name, preload: false, TestContext.Current.CancellationToken);

        Assert.StartsWith("RawDevelop-", thread);
    }

    [Fact]
    public void AFailure_ReachesTheCaller()
    {
        using var queue = new RawDevelopQueue();

        var error = Assert.Throws<InvalidOperationException>(() =>
            queue.Run<int>(() => throw new InvalidOperationException("broken raw"), preload: false, TestContext.Current.CancellationToken));

        Assert.Equal("broken raw", error.Message);
    }

    [Fact]
    public async Task WorkCancelledBeforeItStarts_NeverRuns()
    {
        using var queue = new RawDevelopQueue();
        using var release = new ManualResetEventSlim(false);
        using var cts = new CancellationTokenSource();

        // Hold both threads.
        var blockers = Enumerable.Range(0, 2)
            .Select(_ => Task.Run(() => queue.Run(() => release.Wait(Patience), preload: false, CancellationToken.None)))
            .ToArray();

        var ran = false;
        var waiting = Task.Run(() => queue.Run(() => ran = true, preload: false, cts.Token));

        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waiting);

        release.Set();
        await Task.WhenAll(blockers);

        // Queued after it, so by the time this has run the cancelled job has been passed over.
        queue.Run(() => 0, preload: false, TestContext.Current.CancellationToken);

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
        }, preload: false, cts.Token));

        Assert.True(started.Wait(Patience, TestContext.Current.CancellationToken));
        cts.Cancel();

        Assert.Equal(1, await run);
        Assert.True(finished);
    }

    [Fact]
    public async Task APreload_LeavesAThreadForTheImageOnScreen()
    {
        using var queue = new RawDevelopQueue();
        using var release = new ManualResetEventSlim(false);
        using var preloadStarted = new ManualResetEventSlim(false);

        var preload = Task.Run(() => queue.Run(() =>
        {
            preloadStarted.Set();
            return release.Wait(Patience);
        }, preload: true, CancellationToken.None));

        Assert.True(preloadStarted.Wait(Patience, TestContext.Current.CancellationToken));

        // A second preload must wait; work for the image on screen must not.
        var secondPreloadRan = false;
        var secondPreload = Task.Run(() => queue.Run(() => secondPreloadRan = true, preload: true, CancellationToken.None));

        var onScreen = queue.Run(() => 42, preload: false, TestContext.Current.CancellationToken);

        Assert.Equal(42, onScreen);
        Assert.False(secondPreloadRan);

        release.Set();
        await Task.WhenAll(preload, secondPreload);
        Assert.True(secondPreloadRan);
    }
}