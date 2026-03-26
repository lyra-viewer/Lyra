using SkiaSharp;

namespace Lyra.Renderer.Overlay;

public class CenteredTextOverlay : IOverlay<string>
{
    private readonly SKPaint _textPaint = new()
    {
        Color = SKColors.White,
        IsAntialias = true
    };

    private readonly SKFont _font = FontHelper.GetMonoFont(22);

    public void Render(SKCanvas canvas, float logicalWidth, float logicalHeight, SKColor textColor, string text)
    {
        _textPaint.Color = textColor;

        _font.MeasureText(text, out var textBounds, _textPaint);

        var x = (logicalWidth - textBounds.Width) / 2;
        var y = (logicalHeight + textBounds.Height) / 2;

        canvas.DrawText(text, x, y, SKTextAlign.Left, _font, _textPaint);
    }
}