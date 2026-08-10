using Lyra.UI.SupportingTypes;
using SkiaSharp;

namespace Lyra.UI.Components.Controls;

// ============================================================================
//  ListView<T> - generic flat list with built-in scrolling
// ----------------------------------------------------------------------------
//  Renders a flat list of T items as vertical rows with integrated
//  viewport clipping, scroll offset, and scrollbar.
//
//  Row visuals are produced by a caller-supplied factory delegate.
//
//  API consistent with TreeView<T>:
//    - Data-driven via UpdateData / Remove
//    - Row factory: Func<T, bool isPicked, IComponent>
//    - Optional single-item pick with visual highlight
//    - Bidirectional: click fires Picked, Locate() picks programmatically
//    - Built-in IScrollable - no VScrollContainer wrapper needed
// ============================================================================
public class ListView<T> : ComponentBase, IContainer, IScrollable
{
    private readonly Func<T, bool, IComponent> _rowFactory;
    private List<T> _items;

    private List<(T Item, IComponent Component)> _rows = [];
    private bool _rowsDirty = true;

    // --------------------------------------------------------
    //  Pick state
    // --------------------------------------------------------

    private T? _pickedItem;
    private int _pickedIndex = -1;

    public T? PickedItem => _pickedItem;

    /// Fired when a row is picked by click.
    public event Action<T>? Picked;

    // --------------------------------------------------------
    //  Visual settings
    // --------------------------------------------------------

    /// Vertical spacing between rows.
    public float RowSpacing { get; set; }

    /// Background color for the picked row.
    public SKColor PickedBackground { get; set; } = new(255, 255, 255, 25);

    /// Optional filter - rows where this returns false cannot be picked.
    public Func<T, bool>? CanPick { get; set; }

    // --------------------------------------------------------
    //  Scroll settings
    // --------------------------------------------------------

    private readonly Scrollbar _scrollbar = new();

    public float ScrollSpeed { get; set; } = 40f;

    public ScrollbarStyle ScrollbarStyle
    {
        get => _scrollbar.Style;
        set => _scrollbar.Style = value;
    }

    // --------------------------------------------------------
    //  IScrollable
    // --------------------------------------------------------

    public float ScrollOffset { get; private set; }
    public float ContentSize { get; private set; }
    public float ViewportSize { get; private set; }

    public bool OnScroll(float deltaX, float deltaY)
    {
        if (!((IScrollable)this).NeedsScrollbar)
            return false;

        var previous = ScrollOffset;
        ScrollOffset = Math.Clamp(ScrollOffset - deltaY * ScrollSpeed, 0, ((IScrollable)this).MaxScroll);

        // ReSharper disable once CompareOfFloatsByEqualityOperator
        return ScrollOffset != previous;
    }

    public void ScrollTo(float offset)
    {
        ScrollOffset = Math.Clamp(offset, 0, ((IScrollable)this).MaxScroll);
    }

    public bool ScrollbarContains(SKPoint point) => _scrollbar.Contains(point);

    // --------------------------------------------------------
    //  Constructor
    // --------------------------------------------------------

    public ListView(List<T> items, Func<T, bool, IComponent> rowFactory)
    {
        _items = items;
        _rowFactory = rowFactory;
    }

    // --------------------------------------------------------
    //  IContainer
    // --------------------------------------------------------

    public IReadOnlyList<IComponent> Children =>
        _rows.Select(r => r.Component).ToList();

    public void AddComponent(IComponent child) =>
        throw new NotSupportedException(
            "ListView children are managed by the data model. " +
            "Use UpdateData instead.");

    public void AddComponents(params IComponent[] children) =>
        throw new NotSupportedException(
            "ListView children are managed by the data model. " +
            "Use UpdateData instead.");

    // --------------------------------------------------------
    //  Public API — data management
    // --------------------------------------------------------

    /// Replaces the entire list with new items.
    /// Clears pick state.
    public void UpdateData(List<T> items)
    {
        _items = items;
        _pickedItem = default;
        _pickedIndex = -1;
        _rowsDirty = true;
    }

    /// Removes the first item matching the predicate.
    /// Clears pick if the removed item was picked.
    /// Returns true if an item was removed.
    public bool Remove(Func<T, bool> predicate)
    {
        for (var i = 0; i < _items.Count; i++)
        {
            if (!predicate(_items[i]))
                continue;

            if (i == _pickedIndex)
            {
                _pickedItem = default;
                _pickedIndex = -1;
            }
            else if (i < _pickedIndex)
            {
                _pickedIndex--;
            }

            _items.RemoveAt(i);
            _rowsDirty = true;
            return true;
        }

        return false;
    }

    // --------------------------------------------------------
    //  Public API - navigation
    // --------------------------------------------------------

    /// Finds an item matching the predicate and picks it.
    /// Returns true if a matching item was found.
    public bool Locate(Func<T, bool> predicate)
    {
        for (var i = 0; i < _items.Count; i++)
        {
            if (!predicate(_items[i]))
                continue;

            SetPicked(i);
            return true;
        }

        return false;
    }

    /// Clears the current pick.
    public void ClearPick()
    {
        _pickedItem = default;
        _pickedIndex = -1;
        _rowsDirty = true;
    }

    /// Marks rows for rebuild.
    public void InvalidateRows()
    {
        _rowsDirty = true;
    }

    private void SetPicked(int index)
    {
        if (index == _pickedIndex)
            return;

        _pickedIndex = index;
        _pickedItem = index >= 0 && index < _items.Count ? _items[index] : default;
        _rowsDirty = true;
    }

    // --------------------------------------------------------
    //  Row management
    // --------------------------------------------------------

    private void RebuildRows()
    {
        foreach (var (_, component) in _rows)
            component.Dispose();

        _rows.Clear();

        for (var i = 0; i < _items.Count; i++)
        {
            var item = _items[i];
            var isPicked = i == _pickedIndex;
            var component = _rowFactory(item, isPicked);

            component.Transient = true;
            component.Parent = this;
            
            if (component.VerticalSize != SizeMode.Fixed)
                component.VerticalSize = SizeMode.Shrink;

            _rows.Add((item, component));
        }

        _rowsDirty = false;
    }

    // --------------------------------------------------------
    //  Measure - unconstrained height, track content size
    // --------------------------------------------------------

    protected override SKSize MeasureContent(SKSize availableSize)
    {
        if (_rowsDirty)
            RebuildRows();

        var totalHeight = 0f;
        var maxWidth = 0f;
        var first = true;

        foreach (var (_, component) in _rows)
        {
            if (!first)
                totalHeight += RowSpacing;
            
            first = false;

            component.Measure(new SKSize(availableSize.Width, float.MaxValue));

            totalHeight += component.DesiredSize.Height;
            maxWidth = Math.Max(maxWidth, component.DesiredSize.Width);
        }

        ContentSize = totalHeight;
        return new SKSize(maxWidth, totalHeight);
    }

    // --------------------------------------------------------
    //  Arrange - sequential vertical with scroll offset
    // --------------------------------------------------------

    protected override void ArrangeContent(SKRect contentBounds)
    {
        ViewportSize = contentBounds.Height;
        ScrollOffset = Math.Clamp(ScrollOffset, 0, ((IScrollable)this).MaxScroll);

        var yOffset = 0f;
        var first = true;

        foreach (var (_, component) in _rows)
        {
            if (!first)
                yOffset += RowSpacing;
            
            first = false;

            var rowHeight = component.DesiredSize.Height;

            var contentWidth = component.HorizontalSize == SizeMode.Expand
                ? contentBounds.Width
                : component.DesiredSize.Width;

            var crossOffset = component.HorizontalSize == SizeMode.Expand
                ? 0f
                : component.HorizontalAlign switch
                {
                    HAlign.Center => (contentBounds.Width - contentWidth) / 2f,
                    HAlign.Right => contentBounds.Width - contentWidth,
                    _ => 0f
                };

            component.Arrange(new SKRect(
                contentBounds.Left + crossOffset,
                contentBounds.Top + yOffset - ScrollOffset,
                contentBounds.Left + crossOffset + contentWidth,
                contentBounds.Top + yOffset - ScrollOffset + rowHeight));

            yOffset += rowHeight;
        }
    }

    // --------------------------------------------------------
    //  Render - clipped rows + pick highlight + scrollbar
    // --------------------------------------------------------

    protected override void RenderContent(SKCanvas canvas, SKRect contentBounds)
    {
        canvas.Save();
        canvas.ClipRect(contentBounds);

        for (var i = 0; i < _rows.Count; i++)
        {
            var (_, component) = _rows[i];

            // Skip rows entirely outside viewport.
            if (component.ArrangedBounds.Bottom < contentBounds.Top ||
                component.ArrangedBounds.Top > contentBounds.Bottom)
                continue;

            // Pick highlight — full width.
            if (i == _pickedIndex)
            {
                var highlightRect = new SKRect(
                    contentBounds.Left, component.ArrangedBounds.Top,
                    contentBounds.Right, component.ArrangedBounds.Bottom);

                using var highlightPaint = new SKPaint
                {
                    Color = PickedBackground,
                    IsAntialias = true
                };
                canvas.DrawRect(highlightRect, highlightPaint);
            }

            component.Render(canvas);
        }

        canvas.Restore();

        _scrollbar.Draw(canvas, contentBounds, this);
    }

    // --------------------------------------------------------
    //  Input - click to pick
    // --------------------------------------------------------

    private bool _isPressed;

    protected override void OnPointerDownCore(SKPoint point)
    {
        if (_scrollbar.OnPointerDown(point, this))
            return;

        _isPressed = true;
    }

    protected override void OnPointerMoveCore(SKPoint point)
    {
        _scrollbar.OnPointerMove(point, this);
    }

    protected override void OnPointerUpCore(SKPoint point)
    {
        if (_scrollbar.OnPointerUp())
            return;

        if (!_isPressed)
            return;

        _isPressed = false;

        for (var i = 0; i < _rows.Count; i++)
        {
            var (item, component) = _rows[i];
            var bounds = component.ArrangedBounds;

            if (point.Y < bounds.Top || point.Y > bounds.Bottom)
                continue;

            if (CanPick != null && !CanPick(item))
                break;

            SetPicked(i);
            Picked?.Invoke(item);
            break;
        }
    }

    protected override void OnPointerLeaveCore()
    {
        _isPressed = false;
        _scrollbar.OnPointerLeave();
    }

    // --------------------------------------------------------
    //  Dispose
    // --------------------------------------------------------

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            foreach (var (_, component) in _rows)
                component.Dispose();

            _rows.Clear();
        }

        base.Dispose(disposing);
    }
}