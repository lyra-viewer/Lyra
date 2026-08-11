using SkiaSharp;

namespace Lyra.UI.Components;

public interface IScrollable
{
    /// Current scroll position in pixels.
    float ScrollOffset { get; }

    /// Total size of the scrollable content (along scroll axis).
    float ContentSize { get; }

    /// Size of the visible viewport (along scroll axis).
    float ViewportSize { get; }

    /// Maximum scroll offset before hitting the end.
    float MaxScroll => Math.Max(0, ContentSize - ViewportSize);

    /// True when content exceeds viewport.
    bool NeedsScrollbar => ContentSize > ViewportSize;

    /// True when the point falls on the rendered scrollbar.
    /// Hit-testing consults this to give the bar priority over the
    /// children drawn underneath it and over resize-edge zones.
    /// Implementors that draw a bar forward this to Scrollbar.Contains.
    bool ScrollbarContains(SKPoint point) => false;

    /// Handle scroll input along the scroll axis. Positive moves toward the
    /// start of the content. Returns true if consumed; returning false allows
    /// UIContext to bubble to the parent.
    bool OnScroll(float delta);

    /// Scroll to an absolute offset (clamped to [0, MaxScroll]).
    void ScrollTo(float offset);

    /// Convenience - scroll to the beginning.
    void ScrollToTop() => ScrollTo(0);

    /// Convenience - scroll to the end.
    void ScrollToBottom() => ScrollTo(MaxScroll);
}