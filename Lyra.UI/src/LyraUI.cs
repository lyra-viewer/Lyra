using SkiaSharp;

namespace Lyra.UI;

/// <summary>
/// Entry point for the LyraUI layout and rendering pipeline.
///
/// Process iterates each layer in bottom-to-top z-order and runs
/// the four-phase pipeline on its component tree:
/// <list type="number">
///   <item><term>Measure</term><description>Bottom-up, each component reports its desired size.</description></item>
///   <item><term>Resolve</term><description>Top-down, containers redistribute space to Flexible children.</description></item>
///   <item><term>Arrange</term><description>Top-down, each component receives its final bounds.</description></item>
///   <item><term>Render</term><description>Top-down, each component draws to the canvas.</description></item>
/// </list>
///
/// Phases 1–3 only run when the context is dirty.
/// Phase 4 runs every frame for every visible layer.
///
/// Layers whose root is null or not Present are skipped entirely
/// (no layout, no render).
///
/// The canvas must already be scaled to logical coordinates
/// (i.e. canvas.Scale(DisplayScale) applied by the caller).
/// All layout and hit-testing operates in logical space.
/// </summary>
public static class LyraUI
{
    public static void Process(UIContext context, SKCanvas canvas)
    {
        var layers = context.Layers;
        if (layers.Count == 0)
            return;

        var bounds = canvas.LocalClipBounds;
        var size = new SKSize(bounds.Width, bounds.Height);
        var dirty = context.IsDirty;

        // Bottom-to-top: layout (if dirty) then render.
        foreach (var layer in layers)
        {
            if (layer.Root is null || !layer.Root.Present)
                continue;

            if (dirty)
            {
                layer.Root.Measure(size);
                layer.Root.Resolve();
                layer.Root.Arrange(new SKRect(0, 0, size.Width, size.Height));
            }

            layer.Root.Render(canvas);
        }

        if (dirty)
            context.ClearDirty();
    }
}