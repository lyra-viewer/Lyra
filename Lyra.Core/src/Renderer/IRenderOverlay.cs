using SkiaSharp;

namespace Lyra.Renderer;

/// <summary>Drawn over the image at the end of each frame, on a canvas already scaled to content units.</summary>
public interface IRenderOverlay
{
    void Render(SKCanvas canvas);
}