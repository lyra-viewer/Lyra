using SkiaSharp;

namespace Lyra.UI;

/// <summary>
/// Entry point for the LyraUI layout and rendering pipeline.
///
/// Process runs the four-phase pipeline on the component tree:
///   1. Measure  - bottom-up, each component reports its desired size
///   2. Resolve  - top-down, containers redistribute space to Flexible children
///   3. Arrange  - top-down, each component receives its final bounds
///   4. Render   - top-down, each component draws to the canvas
///
/// Phases 1–3 only run when the context is dirty.
/// Phase 4 runs every frame.
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
            context.Root.Resolve();
            context.Root.Arrange(new SKRect(0, 0, size.Width, size.Height));
            context.ClearDirty();
        }

        context.Root.Render(canvas);
    }
}