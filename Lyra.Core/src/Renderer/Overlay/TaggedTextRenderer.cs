using SkiaSharp;

namespace Lyra.Renderer.Overlay;

public sealed class TaggedTextRenderer
{
    private readonly SKPaint _textPaint = new() { Color = SKColors.White, IsAntialias = true };

    // Fixed palette paints
    private readonly SKPaint _extrasPaint = new() { Color = SKColors.Goldenrod, IsAntialias = true };
    private readonly SKPaint _failedPaint = new() { Color = SKColors.Firebrick, IsAntialias = true };
    private readonly SKPaint _debugPaint = new() { Color = SKColors.SeaGreen, IsAntialias = true };

    private readonly Dictionary<char, SKPaint> _tagToPaint;

    public TaggedTextRenderer()
    {
        _tagToPaint = new Dictionary<char, SKPaint>
        {
            ['e'] = _extrasPaint,
            ['f'] = _failedPaint,
            ['d'] = _debugPaint
        };
    }

    public void SetTextColor(SKColor color) => _textPaint.Color = color;

    public float Measure(string text, SKFont font)
    {
        float width = 0f;
        int i = 0;
        var paint = _textPaint;

        while (i < text.Length)
        {
            var tag = TryParseTag(text, i);
            if (tag.HasValue)
            {
                paint = tag.Value == '/'
                    ? _textPaint
                    : _tagToPaint.GetValueOrDefault(tag.Value, _textPaint);

                i += 3; // "<x>"
                continue;
            }

            var nextTag = text.IndexOf('<', i);

            // literal '<'
            if (nextTag == i)
            {
                width += font.MeasureText("<", paint);
                i++;
                continue;
            }

            var end = nextTag == -1 ? text.Length : nextTag;
            if (end > i)
            {
                var chunk = text[i..end];
                width += font.MeasureText(chunk, paint);
                i = end;
            }
        }

        return width;
    }

    public void Draw(SKCanvas canvas, string text, float x, float y, SKFont font)
    {
        int i = 0;
        var paint = _textPaint;

        while (i < text.Length)
        {
            var tag = TryParseTag(text, i);
            if (tag.HasValue)
            {
                paint = tag.Value == '/'
                    ? _textPaint
                    : _tagToPaint.GetValueOrDefault(tag.Value, _textPaint);

                i += 3; // "<x>"
                continue;
            }

            var nextTag = text.IndexOf('<', i);

            // literal '<'
            if (nextTag == i)
            {
                canvas.DrawText("<", x, y, SKTextAlign.Left, font, paint);
                x += font.MeasureText("<", paint);
                i++;
                continue;
            }

            var end = nextTag == -1 ? text.Length : nextTag;
            if (end > i)
            {
                var chunk = text[i..end];
                canvas.DrawText(chunk, x, y, SKTextAlign.Left, font, paint);
                x += font.MeasureText(chunk, paint);
                i = end;
            }
        }
    }

    private static char? TryParseTag(string text, int index)
    {
        if (index + 2 >= text.Length) return null;
        if (text[index] != '<' || text[index + 2] != '>') return null;

        var tag = text[index + 1];
        return tag is 'e' or 'f' or 'd' or '/' ? tag : null;
    }
}