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
            if (_text == value)
                return;

            _text = value;
            _measureDirty = true;

            // Render runs every frame but Measure only when the layout is dirty,
            // so the clip cache is invalidated here rather than relying on a
            // measure pass to come along first.
            _clippedFor = -1f;

            Invalidate();
        }
    }

    private SKColor _color = Palette.Foreground;
    private bool _antialias = true;
    private bool _underline;
    private bool _ellipsize;

    public SKColor Color
    {
        get => _color;
        set => Set(ref _color, value);
    }

    public bool Antialias
    {
        get => _antialias;
        set => Set(ref _antialias, value);
    }

    public bool Underline
    {
        get => _underline;
        set => Set(ref _underline, value);
    }

    /// <summary>
    /// When the arranged width is narrower than the text, cut the text and mark
    /// the cut with an ellipsis instead of drawing past the bounds.
    /// </summary>
    public bool Ellipsize
    {
        get => _ellipsize;
        set => Set(ref _ellipsize, value);
    }

    // Font properties. GetFont notices a change and rebuilds, which also resets
    // the measure and clip caches - these only have to mark the frame dirty.
    private string _fontFamily = Fonts.MonospaceFamily;
    private float _fontSize = 14f;
    private bool _bold;
    private bool _italic;

    public string FontFamily
    {
        get => _fontFamily;
        set => Set(ref _fontFamily, value);
    }

    public float FontSize
    {
        get => _fontSize;
        set => Set(ref _fontSize, value);
    }

    public bool Bold
    {
        get => _bold;
        set => Set(ref _bold, value);
    }

    public bool Italic
    {
        get => _italic;
        set => Set(ref _italic, value);
    }

    // Cached font and measurement
    private SKFont? _font;
    private bool _measureDirty = true;
    private float _textWidth;
    private SKFontMetrics _metrics;

    // What _font was built from, so a property change is noticed on next use.
    private FontKey _builtKey;

    // Cached ellipsis result. Truncation is a search over the string, and Render
    // runs every frame, so it is recomputed only when an input to it changes.
    private const string Ellipsis = "…";
    private string? _clippedText;
    private float _clippedWidth;
    private float _clippedFor = -1f;

    public Label(string text)
    {
        _text = text;
        Transient = true;
    }

    // --------------------------------------------------------
    //  Static helpers
    // --------------------------------------------------------

    /// <summary>
    /// Everything that decides which SKFont a piece of text needs.
    /// </summary>
    private readonly record struct FontKey(string Family, float Size, SKFontStyleWeight Weight, SKFontStyleSlant Slant)
    {
        public static FontKey For(string family, float size, bool bold, bool italic) =>
            new(family,
                size,
                bold ? SKFontStyleWeight.Bold : SKFontStyleWeight.Normal,
                italic ? SKFontStyleSlant.Italic : SKFontStyleSlant.Upright
            );
        
        public SKFont CreateFont() => new(Fonts.Resolve(Family, Weight, Slant), Size);
    }

    // Fonts for the static measure helper, keyed by the properties that affect
    // advance width. Callers measure whole columns in a loop - one call per EXIF
    // entry, per debug row - and building a typeface plus font plus paint for
    // each one made a cheap query into a font-resolution round trip.
    private static readonly Dictionary<FontKey, SKFont> MeasureFonts = [];
    private static readonly Lock MeasureFontsLock = new();

    /// <summary>
    /// Measures the rendered width of a string without instantiating a Label.
    /// Useful for pre-computing column widths for table-like layouts.
    /// </summary>
    public static float MeasureTextWidth(string text, string? fontFamily = null, float fontSize = 14f, bool bold = false, bool italic = false)
    {
        var key = FontKey.For(fontFamily ?? Fonts.MonospaceFamily, fontSize, bold, italic);

        lock (MeasureFontsLock)
        {
            if (!MeasureFonts.TryGetValue(key, out var font))
            {
                font = key.CreateFont();
                MeasureFonts[key] = font;
            }

            return font.MeasureText(text);
        }
    }

    // --------------------------------------------------------
    //  Layout
    // --------------------------------------------------------

    private SKFont GetFont()
    {
        var key = FontKey.For(FontFamily, FontSize, Bold, Italic);
        if (_font is not null && _builtKey == key)
            return _font;

        _font?.Dispose();
        _font = key.CreateFont();
        _builtKey = key;

        // The old measurement and any clipped text were for the previous font.
        _measureDirty = true;
        _clippedFor = -1f;

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
            _clippedFor = -1f;
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

        var text = Text;
        var textWidth = _textWidth;

        if (Ellipsize && _textWidth > contentBounds.Width)
        {
            (text, textWidth) = Clip(font, contentBounds.Width);

            if (text.Length == 0)
                return;
        }

        // Horizontal alignment within content bounds
        var textX = HorizontalAlign switch
        {
            HAlign.Center => contentBounds.MidX - textWidth / 2f,
            HAlign.Right => contentBounds.Right - textWidth,
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
        canvas.DrawText(text, textX, textY, SKTextAlign.Left, font, paint);

        if (Underline)
        {
            var underlineY = textY + _metrics.Descent + 1f;
            paint.IsStroke = true;
            paint.StrokeWidth = 1f;
            canvas.DrawLine(textX, underlineY, textX + textWidth, underlineY, paint);
        }
    }

    // --------------------------------------------------------
    //  Ellipsis
    // --------------------------------------------------------

    /// <summary>
    /// Longest prefix of the text that fits in <paramref name="maxWidth"/> once the
    /// ellipsis is appended, with its rendered width.
    /// </summary>
    private (string Text, float Width) Clip(SKFont font, float maxWidth)
    {
        // ReSharper disable once CompareOfFloatsByEqualityOperator
        if (_clippedText is not null && _clippedFor == maxWidth)
            return (_clippedText, _clippedWidth);

        var ellipsisWidth = font.MeasureText(Ellipsis);
        if (ellipsisWidth > maxWidth)
        {
            _clippedText = string.Empty;
            _clippedWidth = 0f;
            _clippedFor = maxWidth;
            return (_clippedText, _clippedWidth);
        }

        var budget = maxWidth - ellipsisWidth;

        var low = 0;
        var high = Text.Length;

        while (low < high)
        {
            var mid = (low + high + 1) / 2;
            if (font.MeasureText(Text.AsSpan(0, mid)) <= budget)
                low = mid;
            else
                high = mid - 1;
        }

        _clippedText = Text[..low] + Ellipsis;
        _clippedWidth = font.MeasureText(_clippedText);
        _clippedFor = maxWidth;

        return (_clippedText, _clippedWidth);
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