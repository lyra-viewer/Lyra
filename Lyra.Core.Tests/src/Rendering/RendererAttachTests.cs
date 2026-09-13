using Lyra.Common.Events;
using Lyra.Common.Settings;
using Lyra.DropStatusProvider;
using Lyra.Renderer.Backends;
using Lyra.Renderer.Drawing;
using Lyra.SdlCore;
using SkiaSharp;
using Xunit;

namespace Lyra.Core.Tests.Rendering;

/// <summary>
/// When a renderer starts listening to the window, and when it stops.
/// </summary>
public class RendererAttachTests
{
    [Fact]
    public void ConstructingARendererDoesNotSubscribeIt()
    {
        using var renderer = new FakeRenderer();

        Publish(1920, 1080);

        Assert.Equal(0, renderer.ResizeCount);
    }

    [Fact]
    public void AttachingStartsTheSubscription()
    {
        using var renderer = new FakeRenderer();
        renderer.Attach();

        Publish(1920, 1080);

        Assert.Equal(1, renderer.ResizeCount);
    }

    [Fact]
    public void DisposingStopsIt()
    {
        var renderer = new FakeRenderer();
        renderer.Attach();
        renderer.Dispose();

        Publish(1920, 1080);

        Assert.Equal(0, renderer.ResizeCount);
    }
    
    [Fact]
    public void AttachingIsIdempotent()
    {
        using var renderer = new FakeRenderer();
        renderer.Attach();
        renderer.Attach();

        Publish(1920, 1080);

        Assert.Equal(1, renderer.ResizeCount);
    }

    [Fact]
    public void DisposingWithoutAttachingIsHarmless()
    {
        var renderer = new FakeRenderer();

        renderer.Dispose();

        Publish(1920, 1080);
        Assert.Equal(0, renderer.ResizeCount);
    }

    private static void Publish(int width, int height) => EventManager.Publish(new DrawableSizeChangedEvent(width, height, 1f, 1f));

    /// <summary>A renderer with no GPU behind it: enough of one to be subscribed and counted.</summary>
    private sealed class FakeRenderer() : SkiaRendererBase(new PixelSize(800, 600, 1f, 1f), new NoDrops(), new ViewState(UISettings.DefaultUiSettings), "Fake")
    {
        public int ResizeCount { get; private set; }

        protected override SurfaceProfile Surface => SurfaceProfile.Unknown;

        protected override SKSurface? CreateSurface() => null;

        protected override void OnDrawableSizeChangedInternal(int width, int height, float scale) => ResizeCount++;
    }

    private sealed class NoDrops : IDropProgressProvider
    {
        public DropProgress GetDropStatus() => default;
    }
}