using SkiaSharp;

namespace Lyra.UI;

/// <summary>
/// Entry point for the LyraUI layout and rendering pipeline.
///
/// Process runs the three-phase pipeline on the component tree:
///   1. Measure  - bottom-up, each component reports its desired size
///   2. Arrange  - top-down, each component receives its final bounds
///   3. Render   - top-down, each component draws to the canvas
///
/// Measure and Arrange only run when the context is dirty.
/// Render runs every frame.
///
/// The canvas must already be scaled to logical coordinates
/// (i.e. canvas.Scale(DisplayScale) applied by the caller).
/// All layout and hit-testing operates in logical space.
/// </summary>
public static class LyraUI
{
    public static void Process(UIContext context, SKCanvas canvas)
    {
        if (context.Root == null)
            return;

        var bounds = canvas.LocalClipBounds;
        var size = new SKSize(bounds.Width, bounds.Height);

        if (context.IsDirty)
        {
            context.Root.Measure(size);
            context.Root.Arrange(new SKRect(0, 0, size.Width, size.Height));
            context.ClearDirty();
        }

        context.Root.Render(canvas);
    }
}