using Lyra.UI.SupportingTypes;
using Lyra.UI.Theme;
using SkiaSharp;

namespace Lyra.UI.Components.Primitives;

public class Label : ComponentBase
{
    private string _text;

    public string Text
    {
        get => _text;
        set
        {
            _text = value;
            _measureDirty = true;
        }
    }

    public SKColor Color { get; set; } = Palette.Foreground;
    public bool Antialias { get; set; } = true;
    public bool Underline { get; set; }

    // Font properties
    public string FontFamily { get; set; } = "Menlo";
    public float FontSize { get; set; } = 14f;
    public bool Bold { get; set; }
    public bool Italic { get; set; }

    // Cached font and measurement
    private SKFont? _font;
    private bool _measureDirty = true;
    private float _textWidth;
    private SKFontMetrics _metrics;

    // Tracks what the font was built with
    private string _builtFamily = "";
    private float _builtSize;
    private bool _builtBold;
    private bool _builtItalic;

    public Label(string text)
    {
        _text = text;
        Transient = true;
    }

    // --------------------------------------------------------
    //  Static helpers
    // --------------------------------------------------------

    /// <summary>
    /// Measures the rendered width of a string without instantiating a Label.
    /// Useful for pre-computing column widths for table-like layouts.
    /// </summary>
    public static float MeasureTextWidth(
        string text,
        string fontFamily = "Menlo",
        float fontSize = 14f,
        bool bold = false,
        bool italic = false)
    {
        var style = (bold, italic) switch
        {
            (true, true) => SKFontStyle.BoldItalic,
            (true, false) => SKFontStyle.Bold,
            (false, true) => SKFontStyle.Italic,
            _ => SKFontStyle.Normal
        };

        using var typeface = SKTypeface.FromFamilyName(fontFamily, style);
        using var font = new SKFont(typeface, fontSize);
        using var paint = new SKPaint();
        return font.MeasureText(text, paint);
    }

    // --------------------------------------------------------
    //  Layout
    // --------------------------------------------------------

    private SKFont GetFont()
    {
        if (_font == null
            || _builtFamily != FontFamily
            || _builtSize != FontSize
            || _builtBold != Bold
            || _builtItalic != Italic)
        {
            _font?.Dispose();

            var style = (Bold, Italic) switch
            {
                (true, true) => SKFontStyle.BoldItalic,
                (true, false) => SKFontStyle.Bold,
                (false, true) => SKFontStyle.Italic,
                _ => SKFontStyle.Normal
            };

            var typeface = SKTypeface.FromFamilyName(FontFamily, style);
            _font = new SKFont(typeface, FontSize);

            _builtFamily = FontFamily;
            _builtSize = FontSize;
            _builtBold = Bold;
            _builtItalic = Italic;
            _measureDirty = true;
        }

        return _font;
    }

    protected override SKSize MeasureContent(SKSize availableSize)
    {
        var font = GetFont();

        if (_measureDirty)
        {
            using var paint = new SKPaint();
            _textWidth = font.MeasureText(Text, paint);
            font.GetFontMetrics(out _metrics);
            _measureDirty = false;
        }

        var textHeight = _metrics.Descent - _metrics.Ascent;
        return new SKSize(_textWidth, textHeight);
    }

    protected override void ArrangeContent(SKRect contentBounds) { }

    // --------------------------------------------------------
    //  Render
    // --------------------------------------------------------

    protected override void RenderContent(SKCanvas canvas, SKRect contentBounds)
    {
        var font = GetFont();

        // Horizontal alignment within content bounds
        var textX = HorizontalAlign switch
        {
            HAlign.Center => contentBounds.MidX - _textWidth / 2f,
            HAlign.Right => contentBounds.Right - _textWidth,
            _ => contentBounds.Left
        };

        // Vertical alignment — baseline positioned
        var textY = VerticalAlign switch
        {
            VAlign.Center => contentBounds.MidY + Math.Abs(_metrics.CapHeight) / 2f,
            VAlign.Bottom => contentBounds.Bottom - _metrics.Descent,
            _ => contentBounds.Top - _metrics.Ascent
        };

        using var paint = new SKPaint();
        paint.Color = Color;
        paint.IsAntialias = Antialias;
        canvas.DrawText(Text, textX, textY, font, paint);

        if (Underline)
        {
            var underlineY = textY + _metrics.Descent + 1f;
            paint.IsStroke = true;
            paint.StrokeWidth = 1f;
            canvas.DrawLine(textX, underlineY, textX + _textWidth, underlineY, paint);
        }
    }

    // --------------------------------------------------------
    //  Dispose
    // --------------------------------------------------------

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            _font?.Dispose();

        base.Dispose(disposing);
    }
}