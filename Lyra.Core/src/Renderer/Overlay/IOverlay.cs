using SkiaSharp;

namespace Lyra.Renderer.Overlay;

public interface IOverlay<in T>
{
    void Render(SKCanvas canvas, float logicalWidth, float logicalHeight, SKColor textColor, T data);
}