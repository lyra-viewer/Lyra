using Lyra.Common.Events;
using Lyra.Imaging.Content;
using Lyra.SdlCore;
using SkiaSharp;

namespace Lyra.Renderer;

public interface IRenderer : IDisposable, IDrawableSizeAware
{
    void Render();
    
    void SetComposite(Composite? composite);
    void SetOffset(SKPoint offset);
    void SetDisplayMode(DisplayMode displayMode);
    void SetZoom(int zoomPercentage);
    
    void ToggleSampling();
    void ToggleBackground();
    void ToggleInfo();
}