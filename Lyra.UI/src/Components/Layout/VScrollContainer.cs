using Lyra.UI.SupportingTypes;
using SkiaSharp;

namespace Lyra.UI.Components.Layout;

public class VScrollContainer : ComponentBase, IContainer, IScrollable
{
    private readonly List<IComponent> _children = [];
    public IReadOnlyList<IComponent> Children => _children;

    private readonly Scrollbar _scrollbar = new();

    private float _spacing;

    public float Spacing
    {
        get => _spacing;
        set => Set(ref _spacing, value);
    }

    public float ScrollSpeed { get; set; } = 40f;

    public ScrollbarStyle ScrollbarStyle
    {
        get => _scrollbar.Style;
        set => _scrollbar.Style = value;
    }

    public bool ScrollbarContains(SKPoint point) => _scrollbar.Contains(point);

    public float ScrollOffset { get; private set; }
    public float ContentSize { get; private set; }
    public float ViewportSize { get; private set; }

    public bool OnScroll(float delta)
    {
        if (!((IScrollable)this).NeedsScrollbar)
            return false;

        var previous = ScrollOffset;
        ScrollOffset = Math.Clamp(ScrollOffset - delta * ScrollSpeed, 0, ((IScrollable)this).MaxScroll);

        // ReSharper disable once CompareOfFloatsByEqualityOperator
        return ScrollOffset != previous;
    }

    public void ScrollTo(float offset)
    {
        ScrollOffset = Math.Clamp(offset, 0, ((IScrollable)this).MaxScroll);
    }

    public void AddComponent(IComponent child)
    {
        child.Parent = this;
        _children.Add(child);
    }

    public void AddComponents(params IComponent[] children)
    {
        foreach (var child in children)
            AddComponent(child);
    }

    /// <summary>Disposes and removes all children, resetting the scroll position.</summary>
    public void Clear()
    {
        foreach (var child in _children)
            child.Dispose();

        _children.Clear();
        ScrollOffset = 0;
    }

    protected override SKSize MeasureContent(SKSize availableSize)
    {
        var totalHeight = 0f;
        var maxWidth = 0f;
        var first = true;

        foreach (var child in _children)
        {
            if (!child.Present)
                continue;

            if (!first)
                totalHeight += Spacing;

            first = false;

            child.Measure(new SKSize(availableSize.Width, float.MaxValue));
            totalHeight += child.DesiredSize.Height;
            maxWidth = Math.Max(maxWidth, child.DesiredSize.Width);
        }

        ContentSize = totalHeight;
        return new SKSize(maxWidth, totalHeight);
    }

    protected override void ResolveContent()
    {
        foreach (var child in _children)
        {
            if (child.Present)
                child.Resolve();
        }
    }

    protected override void ArrangeContent(SKRect contentBounds)
    {
        ViewportSize = contentBounds.Height;
        ScrollOffset = Math.Clamp(ScrollOffset, 0, ((IScrollable)this).MaxScroll);

        var yOffset = 0f;
        var first = true;

        foreach (var child in _children)
        {
            if (!child.Present)
                continue;

            if (!first)
                yOffset += Spacing;

            first = false;

            var childWidth = child.HorizontalSize == SizeMode.Expand
                ? contentBounds.Width
                : child.DesiredSize.Width;

            var crossOffset = child.HorizontalSize == SizeMode.Expand
                ? 0f
                : child.HorizontalAlign switch
                {
                    HAlign.Center => (contentBounds.Width - childWidth) / 2f,
                    HAlign.Right => contentBounds.Width - childWidth,
                    _ => 0f
                };

            child.Arrange(new SKRect(
                contentBounds.Left + crossOffset,
                contentBounds.Top + yOffset - ScrollOffset,
                contentBounds.Left + crossOffset + childWidth,
                contentBounds.Top + yOffset - ScrollOffset + child.DesiredSize.Height));

            yOffset += child.DesiredSize.Height;
        }

        // Publish the bar's hit region now, not at Draw - input arrives between frames.
        _scrollbar.UpdateLayout(contentBounds, this);
    }

    protected override void RenderContent(SKCanvas canvas, SKRect contentBounds)
    {
        canvas.Save();
        canvas.ClipRect(contentBounds);

        foreach (var child in _children)
        {
            if (!child.Present)
                continue;

            if (child.ArrangedBounds.Bottom < contentBounds.Top || child.ArrangedBounds.Top > contentBounds.Bottom)
                continue;

            child.Render(canvas);
        }

        canvas.Restore();

        _scrollbar.Draw(canvas, contentBounds, this);
    }

    // --------------------------------------------------------
    //  Input - scrollbar drag
    // --------------------------------------------------------

    protected override void OnPointerDownCore(SKPoint point)
    {
        _scrollbar.OnPointerDown(point, this);
    }

    protected override void OnPointerMoveCore(SKPoint point)
    {
        _scrollbar.OnPointerMove(point, this);
    }

    protected override void OnPointerUpCore(SKPoint point)
    {
        _scrollbar.OnPointerUp();
    }

    protected override void OnPointerLeaveCore()
    {
        _scrollbar.OnPointerLeave();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            foreach (var child in _children)
                child.Dispose();

        base.Dispose(disposing);
    }
}