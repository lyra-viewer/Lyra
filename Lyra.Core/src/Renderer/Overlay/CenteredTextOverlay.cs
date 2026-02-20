using Lyra.SdlCore;
using SkiaSharp;

namespace Lyra.Renderer.Overlay;

public class CenteredTextOverlay : IOverlay<string>
{
    private readonly SKPaint _textPaint = new()
    {
        Color = SKColors.White,
        IsAntialias = true
    };

    public float Scale { get; set; }
    public SKFont? Font { get; set; }

    public CenteredTextOverlay()
    {
        ReloadFont();
    }

    public void ReloadFont()
    {
        Font = FontHelper.GetScaledMonoFont(22, Scale);
    }

    public void Render(SKCanvas canvas, PixelSize drawableBounds, SKColor textPaint, string text)
    {
        if (Font == null)
            return;

        _textPaint.Color = textPaint;

        Font.MeasureText(text, out var imageBounds, _textPaint);

        var x = (drawableBounds.PixelWidth - imageBounds.Width) / 2;
        var y = (drawableBounds.PixelHeight + imageBounds.Height) / 2;

        canvas.DrawText(text, x, y, SKTextAlign.Left, Font, _textPaint);
    }
}