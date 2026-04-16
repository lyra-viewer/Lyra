using Lyra.UI.Theme;
using SkiaSharp;

namespace Lyra.UI.Components;

public static class ScrollbarDrawer
{
    public static void Draw(SKCanvas canvas, SKRect viewportBounds, IScrollable scrollable, ScrollbarStyle style)
    {
        if (!scrollable.NeedsScrollbar)
            return;

        var trackLeft = viewportBounds.Right - style.Width - style.Margin;
        var trackTop = viewportBounds.Top + style.Margin;
        var trackBottom = viewportBounds.Bottom - style.Margin;
        var trackHeight = trackBottom - trackTop;

        var trackRect = new SKRect(trackLeft, trackTop, trackLeft + style.Width, trackBottom);

        using var trackPaint = new SKPaint
        {
            Color = style.TrackColor,
            IsAntialias = true
        };
        canvas.DrawRoundRect(trackRect, style.CornerRadius, style.CornerRadius, trackPaint);

        var viewportRatio = scrollable.ViewportSize / scrollable.ContentSize;
        var thumbHeight = Math.Max(style.MinThumbHeight, trackHeight * viewportRatio);

        var maxScroll = scrollable.MaxScroll;
        var scrollRatio = maxScroll > 0 ? scrollable.ScrollOffset / maxScroll : 0f;
        var thumbTop = trackTop + scrollRatio * (trackHeight - thumbHeight);

        var thumbRect = new SKRect(trackLeft, thumbTop,
            trackLeft + style.Width, thumbTop + thumbHeight);

        using var thumbPaint = new SKPaint
        {
            Color = style.ThumbColor,
            IsAntialias = true
        };
        canvas.DrawRoundRect(thumbRect, style.CornerRadius, style.CornerRadius, thumbPaint);
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

    public static ScrollbarStyle Default => new()
    {
        Width = 6f,
        Margin = 2f,
        MinThumbHeight = 20f,
        CornerRadius = 3f,
        TrackColor = Palette.HoverSubtle,
        ThumbColor = Palette.Foreground.WithAlpha(160)
    };
}