using Lyra.UI.Theme;
using SkiaSharp;

namespace Lyra.UI.Components;

public sealed class Scrollbar
{
    public ScrollbarStyle Style { get; set; } = ScrollbarStyle.Default;

    private SKRect _track;
    private SKRect _thumb;
    private bool _visible;

    private bool _dragging;
    private bool _hovered;

    /// Pointer offset inside the thumb at the moment of the grab, so the
    /// thumb keeps its position under the cursor instead of jumping.
    private float _grabOffset;

    public bool IsDragging => _dragging;

    // --------------------------------------------------------
    //  Layout
    // --------------------------------------------------------

    /// <summary>
    /// Publishes the bar's geometry for the current layout. Hosts call this from
    /// ArrangeContent.
    ///
    /// This has to happen in Arrange rather than Draw, because hit-testing reads
    /// the geometry: input arrives between frames, so a bar positioned during
    /// Draw answers Contains() with wherever it was on the *previous* frame. A
    /// panel that just resized or expanded would take clicks in the old place.
    /// </summary>
    public void UpdateLayout(SKRect viewportBounds, IScrollable scrollable)
    {
        _visible = scrollable.NeedsScrollbar;

        if (!_visible)
        {
            _track = SKRect.Empty;
            _thumb = SKRect.Empty;
            _dragging = false;
            _hovered = false;
            return;
        }

        UpdateGeometry(viewportBounds, scrollable);
    }

    // --------------------------------------------------------
    //  Render
    // --------------------------------------------------------

    public void Draw(SKCanvas canvas, SKRect viewportBounds, IScrollable scrollable)
    {
        // Recomputed here as well as in UpdateLayout: ScrollOffset changes during
        // a drag without a re-arrange, so the thumb must track it within a frame.
        UpdateLayout(viewportBounds, scrollable);

        if (!_visible)
            return;

        using var paint = new SKPaint();
        paint.IsAntialias = true;

        paint.Color = Style.TrackColor;
        canvas.DrawRoundRect(_track, Style.CornerRadius, Style.CornerRadius, paint);

        paint.Color = _dragging || _hovered ? Style.ThumbHoverColor : Style.ThumbColor;
        canvas.DrawRoundRect(_thumb, Style.CornerRadius, Style.CornerRadius, paint);
    }

    private void UpdateGeometry(SKRect viewportBounds, IScrollable scrollable)
    {
        var left = viewportBounds.Right - Style.Width - Style.Margin;
        var top = viewportBounds.Top + Style.Margin;
        var bottom = viewportBounds.Bottom - Style.Margin;

        _track = new SKRect(left, top, left + Style.Width, Math.Max(top, bottom));

        var contentSize = scrollable.ContentSize;
        var viewportRatio = contentSize > 0 ? scrollable.ViewportSize / contentSize : 1f;

        var thumbHeight = Math.Clamp(
            _track.Height * viewportRatio,
            Math.Min(Style.MinThumbHeight, _track.Height),
            _track.Height
        );

        var maxScroll = scrollable.MaxScroll;
        var scrollRatio = maxScroll > 0
            ? Math.Clamp(scrollable.ScrollOffset / maxScroll, 0f, 1f)
            : 0f;

        var thumbTop = _track.Top + scrollRatio * (_track.Height - thumbHeight);

        _thumb = new SKRect(_track.Left, thumbTop, _track.Right, thumbTop + thumbHeight);
    }

    // --------------------------------------------------------
    //  Hit-testing
    // --------------------------------------------------------

    /// <summary>
    /// True when the point falls on the rendered bar. UIContext reaches
    /// this through IScrollable.ScrollbarContains to give the bar priority
    /// over the children drawn underneath it and over resize-edge zones.
    /// </summary>
    public bool Contains(SKPoint point) => _visible && _track.Contains(point);

    // --------------------------------------------------------
    //  Input - true means the event belonged to the bar
    // --------------------------------------------------------

    public bool OnPointerDown(SKPoint point, IScrollable scrollable)
    {
        if (!_visible || !_track.Contains(point))
            return false;

        if (_thumb.Contains(point))
        {
            _grabOffset = point.Y - _thumb.Top;
        }
        else
        {
            _grabOffset = _thumb.Height / 2f;
            ApplyThumbTop(point.Y - _grabOffset, scrollable);
        }

        _dragging = true;
        return true;
    }

    public bool OnPointerMove(SKPoint point, IScrollable scrollable)
    {
        _hovered = _visible && _track.Contains(point);

        if (!_dragging)
            return false;

        ApplyThumbTop(point.Y - _grabOffset, scrollable);
        return true;
    }

    public bool OnPointerUp()
    {
        var wasDragging = _dragging;
        _dragging = false;
        return wasDragging;
    }

    /// <summary>
    /// Clears hover. Deliberately does not cancel an active drag - on a bar
    /// this thin the pointer routinely leaves the host mid-drag, and pointer
    /// capture keeps the moves coming.
    /// </summary>
    public void OnPointerLeave() => _hovered = false;

    private void ApplyThumbTop(float thumbTop, IScrollable scrollable)
    {
        var range = _track.Height - _thumb.Height;
        if (range <= 0.01f)
            return;

        var ratio = Math.Clamp((thumbTop - _track.Top) / range, 0f, 1f);
        scrollable.ScrollTo(ratio * scrollable.MaxScroll);
    }
}

public struct ScrollbarStyle
{
    public float Width { get; set; }
    public float Margin { get; set; }
    public float MinThumbHeight { get; set; }
    public float CornerRadius { get; set; }
    public SKColor TrackColor { get; set; }
    public SKColor ThumbColor { get; set; }

    /// Thumb color while hovered or dragged.
    public SKColor ThumbHoverColor { get; set; }

    public static ScrollbarStyle Default => new()
    {
        Width = 7f,
        Margin = 2f,
        MinThumbHeight = 20f,
        CornerRadius = 0f,
        TrackColor = Palette.HoverSubtle,
        ThumbColor = Palette.Foreground.WithAlpha(160),
        ThumbHoverColor = Palette.Foreground.WithAlpha(220)
    };
}
