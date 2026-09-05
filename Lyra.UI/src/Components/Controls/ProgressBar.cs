using System.Diagnostics;
using Lyra.UI.Theme;
using SkiaSharp;

namespace Lyra.UI.Components.Controls;

/// <summary>
/// A horizontal progress track, either filled to a known fraction or sweeping when the fraction
/// is not known. Display only - it takes no input and decides nothing about what it shows.
/// </summary>
public sealed class ProgressBar : ComponentBase
{
    private const float TrackHeight = 6f;
    private const float DefaultWidth = 240f;

    /// <summary>How much of the track the indeterminate sweep covers, and how long it takes to cross.</summary>
    private const float SweepFraction = 0.35f;
    private const double SweepSeconds = 1.1;
    
    private const byte TrackAlpha = 60;

    private static readonly double TicksPerSecond = Stopwatch.Frequency;

    private float _value;
    private bool _indeterminate;
    private SKColor _color = Palette.Foreground;

    /// <summary>How full the bar is, from 0 to 1. Clamped; ignored while indeterminate.</summary>
    public float Value
    {
        get => _value;
        set => Set(ref _value, Math.Clamp(value, 0f, 1f));
    }

    /// <summary>Sweeps instead of filling, for when there is no usable estimate of the total.</summary>
    public bool Indeterminate
    {
        get => _indeterminate;
        set => Set(ref _indeterminate, value);
    }

    /// <summary>Color of the filled portion. The track is drawn from this, dimmed.</summary>
    public SKColor Color
    {
        get => _color;
        set => Set(ref _color, value);
    }

    protected override SKSize MeasureContent(SKSize availableSize) => new(Math.Min(DefaultWidth, availableSize.Width), TrackHeight);

    protected override void ArrangeContent(SKRect contentBounds) { }

    protected override void RenderContent(SKCanvas canvas, SKRect cb)
    {
        if (cb.Width <= 0 || cb.Height <= 0)
            return;

        var top = cb.MidY - TrackHeight / 2f;
        var track = new SKRect(cb.Left, top, cb.Right, top + TrackHeight);

        using var paint = new SKPaint();
        paint.IsAntialias = true;

        paint.Color = _color.WithAlpha(TrackAlpha);
        canvas.DrawRect(track, paint);

        var fill = _indeterminate ? SweepBounds(track) : FillBounds(track);
        if (fill.Width <= 0)
            return;

        paint.Color = _color;
        canvas.DrawRect(fill, paint);
    }

    private SKRect FillBounds(SKRect track)
    {
        var width = _value <= 0 ? 0 : Math.Max(TrackHeight, track.Width * _value);
        return new SKRect(track.Left, track.Top, track.Left + width, track.Bottom);
    }

    /// <summary>
    /// A block crossing the track and back. Driven by the wall clock rather than a frame count so
    /// it moves at the same speed whatever the frame rate, and so it needs no state to reset.
    /// </summary>
    private static SKRect SweepBounds(SKRect track)
    {
        var sweep = track.Width * SweepFraction;
        var travel = track.Width - sweep;
        if (travel <= 0)
            return track;

        var seconds = Stopwatch.GetTimestamp() / TicksPerSecond % (SweepSeconds * 2);
        var phase = seconds / SweepSeconds;

        var t = phase <= 1 ? phase : 2 - phase;
        var left = track.Left + (float)(travel * t);

        return new SKRect(left, track.Top, left + sweep, track.Bottom);
    }
}
