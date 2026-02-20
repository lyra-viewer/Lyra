using Lyra.Common.Events;
using Lyra.SdlCore;
using SkiaSharp;

namespace Lyra.Renderer.Overlay;

public interface IOverlay<in T> : IDrawableSizeAware
{
    protected float Scale { get; set; }

    protected SKFont? Font { get; set; }

    void ReloadFont();

    void Render(SKCanvas canvas, PixelSize drawableBounds, SKColor textColor, T data);

    void IDrawableSizeAware.OnDrawableSizeChanged(EventManager.DrawableSizeChangedEvent e)
    {
        const float tolerance = 0.01f;
        var roundedScale = MathF.Round(e.Scale, 2);
        if (MathF.Abs(roundedScale - Scale) > tolerance)
        {
            Font?.Dispose();
            Scale = e.Scale;
            ReloadFont();
        }
    }
}