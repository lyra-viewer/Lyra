using Lyra.Imaging.Content;
using Lyra.Imaging.Content.Status;
using Lyra.Imaging.Loading;
using Lyra.Imaging.Tests.Support;
using Xunit;

namespace Lyra.Imaging.Tests.Loading;

public class CurrentImageLifetimeTests
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);

    [Fact]
    public void TheImageOnScreen_IsDisposed_OnceAJumpLeavesItOutsideTheWindow()
    {
        var a = MinimalImageBuilder.WriteBmp();
        var b = MinimalImageBuilder.WriteBmp();

        try
        {
            using var loader = new ImageLoader(new PreloadTaskScheduler(1));
            var first = Settled(loader.GetImage(a));

            loader.Cleanup([b]);
            Assert.NotEqual(CompositeState.Disposed, first.State);

            var second = loader.GetImage(b);

            Assert.True(SpinWait.SpinUntil(() => first.State == CompositeState.Disposed, Patience), $"State: {first.State}");
            Assert.True(second.IsCurrent);
        }
        finally
        {
            File.Delete(a);
            File.Delete(b);
        }
    }

    [Fact]
    public void ThePurgedImageOnScreen_IsDisposed_WhenTheNextOneOpens()
    {
        var a = MinimalImageBuilder.WriteBmp();
        var b = MinimalImageBuilder.WriteBmp();

        try
        {
            using var loader = new ImageLoader(new PreloadTaskScheduler(1));
            var first = Settled(loader.GetImage(a));

            loader.Purge(a);
            Assert.NotEqual(CompositeState.Disposed, first.State);

            _ = loader.GetImage(b);

            Assert.True(SpinWait.SpinUntil(() => first.State == CompositeState.Disposed, Patience), $"State: {first.State}");
        }
        finally
        {
            File.Delete(a);
            File.Delete(b);
        }
    }

    [Fact]
    public void ANeighbourInTheWindow_StaysCached()
    {
        var a = MinimalImageBuilder.WriteBmp();
        var b = MinimalImageBuilder.WriteBmp();

        try
        {
            using var loader = new ImageLoader(new PreloadTaskScheduler(1));
            var first = Settled(loader.GetImage(a));

            loader.Cleanup([a, b]);
            _ = Settled(loader.GetImage(b));

            Assert.Equal(CompositeState.Complete, first.State);
            Assert.Same(first, loader.GetImage(a));
        }
        finally
        {
            File.Delete(a);
            File.Delete(b);
        }
    }

    [Fact]
    public void AMissingFile_OpensAsATransientNotFound_InsteadOfThrowing()
    {
        var missing = Path.Combine(Path.GetTempPath(), $"lyra-missing-{Guid.NewGuid():N}.png");

        using var loader = new ImageLoader(new PreloadTaskScheduler(1));
        var composite = loader.GetImage(missing);

        Assert.True(SpinWait.SpinUntil(() => composite.State == CompositeState.Failed, Patience), $"State: {composite.State}");
        Assert.Equal(LoadFailureKind.NotFound, composite.Failure?.Kind);
        Assert.True(composite.Failure?.IsTransient);
    }

    private static Composite Settled(Composite composite)
    {
        Assert.True(SpinWait.SpinUntil(() => composite.State == CompositeState.Complete, Patience), $"State: {composite.State}");

        // The load task returns just after the composite reports complete.
        Thread.Sleep(100);
        return composite;
    }
}