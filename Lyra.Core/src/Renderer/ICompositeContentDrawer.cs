using Lyra.Imaging.Content;
using SkiaSharp;

namespace Lyra.Renderer;

public interface ICompositeContentDrawer
{
    void Draw(SKCanvas canvas, Composite composite, SKRect destFullRect, SKRect visibleFullRect, SKSamplingOptions sampling, float zoomScale, float displayScale);
}