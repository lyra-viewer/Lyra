using Lyra.Imaging.Content.Status;
using Lyra.Imaging.Loading;
using Lyra.Imaging.Tests.Support;
using Xunit;

namespace Lyra.Imaging.Tests.Loading;

public class PreloadPromotionTests
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan Settle = TimeSpan.FromMilliseconds(300);

    [Fact]
    public async Task APromotedTask_RunsWhileTheWorkersAreBusy()
    {
        using var scheduler = new PreloadTaskScheduler(1);
        using var release = new ManualResetEventSlim(false);
        using var ran = new ManualResetEventSlim(false);
        var factory = new TaskFactory(scheduler);

        var blocker = factory.StartNew(() => release.Wait(Patience), TestContext.Current.CancellationToken);
        var queued = factory.StartNew(ran.Set, TestContext.Current.CancellationToken);

        Assert.False(ran.Wait(Settle, TestContext.Current.CancellationToken));

        Assert.True(scheduler.Promote(queued));
        Assert.True(ran.Wait(Patience, TestContext.Current.CancellationToken));
        Assert.False(blocker.IsCompleted);

        release.Set();
        await Task.WhenAll(blocker, queued);
    }

    [Fact]
    public async Task ATaskIsNotPromoted_OnceStarted_OrWhenNotQueuedHere()
    {
        using var scheduler = new PreloadTaskScheduler(1);
        var runs = 0;

        var task = new TaskFactory(scheduler).StartNew(() => Interlocked.Increment(ref runs), TestContext.Current.CancellationToken);
        await task;

        Assert.False(scheduler.Promote(task));
        Assert.False(scheduler.Promote(Task.CompletedTask));
        Assert.False(scheduler.Promote(null));
        Assert.Equal(1, runs);
    }

    [Fact]
    public void OpeningAQueuedPreload_LoadsItWithoutWaitingForThePreloadWorkers()
    {
        var scheduler = new PreloadTaskScheduler(1);
        using var release = new ManualResetEventSlim(false);
        var path = MinimalImageBuilder.WriteBmp();

        try
        {
            using var loader = new ImageLoader(scheduler);

            // The only preload worker is busy for as long as the test needs.
            _ = new TaskFactory(scheduler).StartNew(() => release.Wait(Patience), TestContext.Current.CancellationToken);

            loader.PreloadAdjacent([path]);
            Assert.True(loader.IsLoading(path));

            var composite = loader.GetImage(path);

            Assert.True(composite.IsCurrent);
            Assert.True(SpinWait.SpinUntil(() => composite.State == CompositeState.Complete, Patience), $"State: {composite.State}");
            Assert.False(release.IsSet);

            release.Set(); // before the loader joins its workers
        }
        finally
        {
            release.Set();
            File.Delete(path);
        }
    }

    [Fact]
    public void APreloadNotOpened_WaitsForThePreloadWorkers()
    {
        var scheduler = new PreloadTaskScheduler(1);
        using var release = new ManualResetEventSlim(false);
        var path = MinimalImageBuilder.WriteBmp();

        try
        {
            using var loader = new ImageLoader(scheduler);
            _ = new TaskFactory(scheduler).StartNew(() => release.Wait(Patience), TestContext.Current.CancellationToken);

            loader.PreloadAdjacent([path]);
            Thread.Sleep(Settle);

            Assert.True(loader.IsLoading(path));

            release.Set();
        }
        finally
        {
            release.Set();
            File.Delete(path);
        }
    }

    [Fact]
    public void OnlyTheImageOpenedLast_IsCurrent()
    {
        var first = MinimalImageBuilder.WriteBmp();
        var second = MinimalImageBuilder.WriteBmp();

        try
        {
            using var loader = new ImageLoader();

            var a = loader.GetImage(first);
            var b = loader.GetImage(second);

            Assert.False(a.IsCurrent);
            Assert.True(b.IsCurrent);
        }
        finally
        {
            File.Delete(first);
            File.Delete(second);
        }
    }
}