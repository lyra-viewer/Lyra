using Lyra.Imaging.Content;
using Lyra.SdlCore;
using SkiaSharp;

namespace Lyra.Renderer.Overlay;

public partial class ImageInfoOverlay : IOverlay<(Composite? composite, ViewerState states)>
{
    public float Scale { get; set; }
    public SKFont? Font { get; set; }

    private readonly SKPaint _textPaint = new() { Color = SKColors.White, IsAntialias = true };
    private readonly SKPaint _extrasPaint = new() { Color = SKColors.Goldenrod, IsAntialias = true };
    private readonly SKPaint _failedPaint = new() { Color = SKColors.Firebrick, IsAntialias = true };
    private readonly SKPaint _debugPaint = new() { Color = SKColors.SeaGreen, IsAntialias = true };

    private readonly Dictionary<char, SKPaint> _tagToPaint;

    private const int BasePadding = 13;
    private const int BaseLineHeight = 7;

    public ImageInfoOverlay()
    {
        _tagToPaint = new Dictionary<char, SKPaint>
        {
            ['e'] = _extrasPaint,
            ['f'] = _failedPaint,
            ['d'] = _debugPaint
        };

        ReloadFont();
    }

    public void ReloadFont()
    {
        Font = FontHelper.GetScaledMonoFont(14, Scale);
    }

    public void Render(SKCanvas canvas, DrawableBounds drawableBounds, SKColor textPaint, (Composite? composite, ViewerState states) data)
    {
        if (Font == null || data.composite == null) return;

        _textPaint.Color = textPaint;

        var padding = BasePadding * Scale;
        var lineHeight = Font.Size + (BaseLineHeight * Scale);
        var textY = padding + Font.Size;

        foreach (var line in BuildLines(data.composite, data.states))
        {
            DrawTaggedLine(canvas, line, padding, textY);
            textY += lineHeight;
        }
    }

    private void DrawTaggedLine(SKCanvas canvas, string line, float xStart, float y)
    {
        if (Font == null)
            return;

        var x = xStart;
        var i = 0;
        var paint = _textPaint;

        while (i < line.Length)
        {
            var tag = TryParseTag(line, i);
            if (tag.HasValue)
            {
                if (tag.Value == '/')
                    paint = _textPaint;
                else if (_tagToPaint.TryGetValue(tag.Value, out var mapped))
                    paint = mapped;

                i += 3; // advance over "<x>"
                continue;
            }

            var nextTagIdx = line.IndexOf('<', i);
            if (nextTagIdx == i)
            {
                const string literal = "<";
                canvas.DrawText(literal, x, y, SKTextAlign.Left, Font, paint);
                x += Font.MeasureText(literal, paint);
                i += 1;
                continue;
            }

            var end = nextTagIdx == -1 ? line.Length : nextTagIdx;
            if (end > i)
            {
                var chunk = line[i..end];
                canvas.DrawText(chunk, x, y, SKTextAlign.Left, Font, paint);
                x += Font.MeasureText(chunk, paint);
                i = end;
            }
        }
    }

    private static char? TryParseTag(string line, int index)
    {
        if (index + 2 >= line.Length || line[index] != '<')
            return null;

        var middle = line[index + 1];
        if (line[index + 2] != '>')
            return null;

        return middle is 'e' or 'f' or 'd' or '/' ? middle : null;
    }
}