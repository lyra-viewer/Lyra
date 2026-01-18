using Lyra.Common.Events;
using Lyra.SdlCore;
using SkiaSharp;

namespace Lyra.Renderer.Overlay;

public interface IOverlay<in T> : IDisplayScaleAware
{
    protected float Scale { get; set; }
    protected SKFont? Font { get; set; }

    void ReloadFont();
    
    void Render(SKCanvas canvas, DrawableBounds drawableBounds, SKColor textColor, T data);

    void IDisplayScaleAware.OnDisplayScaleChanged(EventManager.DisplayScaleChangedEvent e)
    {
        const float tolerance = 0.01f;
        var roundedScale = MathF.Round(e.Scale, 2);
        if (MathF.Abs(roundedScale - Scale) > tolerance)
        {
            Font?.Dispose();
            Scale = roundedScale;
            ReloadFont();
        }
    }
}