using Lyra.UI.SupportingTypes;
using Lyra.UI.Theme;
using SkiaSharp;

namespace Lyra.UI.Components.Controls;

/// <summary>
/// Horizontal integer slider with fixed min/max endpoints and an always-visible value
/// label floating above the thumb.
/// </summary>
public sealed class ValueSlider : ComponentBase
{
    private const float TrackHeight = 2f;
    private const float NotchHeight = 5f;
    private const float NotchWidth = 1f;
    private const float ThumbRadius = 6f;
    private const float LabelGap = 4f;
    private const float EndLabelFontSize = 11f;
    private const float ValueLabelFontSize = 12f;

    public event Action<int>? ValueChanged;

    private int _value;
    private bool _isDragging;
    
    private readonly SKTypeface _endTypeface;
    private readonly SKFont _endFont;
    private readonly SKFontMetrics _endMetrics;
    private readonly float _endLabelWidth;

    private readonly SKTypeface _valueTypeface;
    private readonly SKFont _valueFont;
    private readonly SKFontMetrics _valueMetrics;
    private readonly float _valueLabelHeight;

    public int Min { get; }
    public int Max { get; }

    public int Value
    {
        get => _value;
        set
        {
            var clamped = Math.Clamp(value, Min, Max);
            if (clamped == _value) return;
            _value = clamped;
            ValueChanged?.Invoke(_value);
        }
    }

    public ValueSlider(int min, int max, int initialValue)
    {
        Min = min;
        Max = max;
        _value = Math.Clamp(initialValue, min, max);
        MinHeight = 44f;

        _endTypeface = SKTypeface.FromFamilyName(Fonts.MonospaceFamily, SKFontStyle.Normal);
        _endFont = new SKFont(_endTypeface, EndLabelFontSize);
        _endFont.GetFontMetrics(out _endMetrics);
        using var p = new SKPaint();
        _endLabelWidth = Math.Max(_endFont.MeasureText(min.ToString(), p), _endFont.MeasureText(max.ToString(), p));

        _valueTypeface = SKTypeface.FromFamilyName(Fonts.MonospaceFamily, SKFontStyle.Normal);
        _valueFont = new SKFont(_valueTypeface, ValueLabelFontSize);
        _valueFont.GetFontMetrics(out _valueMetrics);
        _valueLabelHeight = _valueMetrics.Descent - _valueMetrics.Ascent;
    }

    // --------------------------------------------------------
    //  Layout
    // --------------------------------------------------------

    protected override SKSize MeasureContent(SKSize availableSize)
    {
        var totalHeight = _valueLabelHeight + LabelGap + ThumbRadius * 2 + NotchHeight;
        return new SKSize(availableSize.Width, Math.Max(totalHeight, MinHeight ?? 0));
    }

    protected override void ArrangeContent(SKRect contentBounds) { }

    // --------------------------------------------------------
    //  Track geometry (derived from arranged bounds)
    // --------------------------------------------------------

    private readonly record struct TrackLayout(float Left, float Right, float Y, float ThumbX);

    private TrackLayout TrackGeometry(SKRect cb)
    {
        var left = cb.Left + _endLabelWidth + 6f;
        var right = cb.Right - _endLabelWidth - 6f;
        var y = cb.Top + _valueLabelHeight + LabelGap + ThumbRadius;
        var t = (float)(_value - Min) / (Max - Min);
        return new TrackLayout(left, right, y, left + t * (right - left));
    }

    // --------------------------------------------------------
    //  Render
    // --------------------------------------------------------

    protected override void RenderContent(SKCanvas canvas, SKRect cb)
    {
        var track = TrackGeometry(cb);

        using var paint = new SKPaint { IsAntialias = true };

        // Track
        paint.Color = Palette.Border;
        paint.StrokeWidth = TrackHeight;
        paint.IsStroke = true;
        canvas.DrawLine(track.Left, track.Y, track.Right, track.Y, paint);

        // Filled portion (left of thumb)
        paint.Color = Palette.Muted;
        canvas.DrawLine(track.Left, track.Y, track.ThumbX, track.Y, paint);

        // Notches
        paint.IsStroke = false;
        var steps = Max - Min;
        for (var i = 0; i <= steps; i++)
        {
            var t = (float)i / steps;
            var nx = track.Left + t * (track.Right - track.Left);
            var notchRect = new SKRect(nx - NotchWidth / 2f, track.Y + ThumbRadius - 1f, nx + NotchWidth / 2f, track.Y + ThumbRadius - 1f + NotchHeight);
            paint.Color = i == (_value - Min) ? Palette.Muted : Palette.Border;
            canvas.DrawRect(notchRect, paint);
        }

        // Thumb
        paint.Color = Palette.Accent;
        canvas.DrawCircle(track.ThumbX, track.Y, ThumbRadius, paint);

        // End labels
        DrawEndLabel(canvas, Min.ToString(), track.Left, track.Y, HAlign.Right, paint);
        DrawEndLabel(canvas, Max.ToString(), track.Right, track.Y, HAlign.Left, paint);

        // Floating value label above thumb
        DrawValueLabel(canvas, _value.ToString(), track.ThumbX, track.Y, paint);
    }

    private void DrawEndLabel(SKCanvas canvas, string text, float anchorX, float trackY, HAlign align, SKPaint paint)
    {
        var textW = _endFont.MeasureText(text, paint);
        var x = align == HAlign.Right ? anchorX - 6f - textW : anchorX + 6f;
        var baseline = trackY + Math.Abs(_endMetrics.CapHeight) / 2f;
        paint.Color = Palette.Dim;
        canvas.DrawText(text, x, baseline, SKTextAlign.Left, _endFont, paint);
    }

    private void DrawValueLabel(SKCanvas canvas, string text, float thumbX, float trackY, SKPaint paint)
    {
        var textW = _valueFont.MeasureText(text, paint);
        var x = thumbX - textW / 2f;
        var baseline = trackY - ThumbRadius - LabelGap - _valueMetrics.Descent;
        paint.Color = Palette.Foreground;
        canvas.DrawText(text, x, baseline, SKTextAlign.Left, _valueFont, paint);
    }

    // --------------------------------------------------------
    //  Input
    // --------------------------------------------------------

    public override void OnPointerDown(SKPoint point)
    {
        if (!IsEffectivelyEnabled) return;
        _isDragging = true;
        UpdateValueFromX(point.X);
    }

    public override void OnPointerMove(SKPoint point)
    {
        if (!_isDragging) return;
        UpdateValueFromX(point.X);
    }

    public override void OnPointerUp(SKPoint point)
    {
        _isDragging = false;
    }

    public override void OnPointerLeave()
    {
        _isDragging = false;
    }

    private void UpdateValueFromX(float x)
    {
        var track = TrackGeometry(ContentBounds);
        var t = Math.Clamp((x - track.Left) / (track.Right - track.Left), 0f, 1f);
        var newValue = Min + (int)Math.Round(t * (Max - Min));
        Value = newValue;
    }

    // --------------------------------------------------------
    //  Dispose
    // --------------------------------------------------------

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _endFont.Dispose();
            _endTypeface.Dispose();
            _valueFont.Dispose();
            _valueTypeface.Dispose();
        }

        base.Dispose(disposing);
    }
}