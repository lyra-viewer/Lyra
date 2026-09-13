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

    // Stable backing list for the Children property. Rebuilt alongside _rows
    // rather than projected per access: hit-testing indexes Children inside a
    // loop, so a projecting property allocates once per child per traversal,
    // and every pointer move runs three traversals.
    private readonly List<IComponent> _childrenView = [];

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
    //  Virtualization
    // --------------------------------------------------------

    /// <summary>
    /// Builds components only for the rows that overlap the viewport, instead of one per item.
    /// </summary>
    public bool Virtualized { get; set; }

    /// <summary>Index of the first item <see cref="_rows"/> holds. Always 0 when not virtualized.</summary>
    private int _windowStart;

    /// <summary>Uniform row height, measured from a probe row. Negative until measured.</summary>
    private float _rowHeight = -1;

    /// <summary>Width the probe was measured at; a change in width invalidates the height.</summary>
    private float _measuredWidth = -1;

    /// <summary>Desired width of the probe row, reported as this control's own.</summary>
    private float _rowWidth;

    /// <summary>Rows to build beyond each edge of the viewport, so a scroll does not tear.</summary>
    private const int Overscan = 2;

    /// <summary>Height of one row plus the gap after it.</summary>
    private float RowPitch => _rowHeight + RowSpacing;

    /// <summary>Total height of every item, whether it has a component.</summary>
    private float VirtualContentSize => _items.Count == 0 ? 0 : _items.Count * RowPitch - RowSpacing;

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

    public bool ScrollbarContains(SKPoint point) => _scrollbar.Contains(point);

    // --------------------------------------------------------
    //  Constructor
    // --------------------------------------------------------

    /// <param name="items">Copied on entry. The control keeps its own list so that
    /// later edits to the caller's list cannot desync the rows and pick index from
    /// the data behind them - call UpdateData to publish changes.</param>
    public ListView(List<T> items, Func<T, bool, IComponent> rowFactory)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(rowFactory);

        _items = [.. items];
        _rowFactory = rowFactory;
    }

    // --------------------------------------------------------
    //  IContainer
    // --------------------------------------------------------

    public IReadOnlyList<IComponent> Children => _childrenView;

    public void AddComponent(IComponent child) =>
        throw new NotSupportedException("ListView children are managed by the data model. Use UpdateData instead.");

    public void AddComponents(params IComponent[] children) =>
        throw new NotSupportedException("ListView children are managed by the data model. Use UpdateData instead.");

    // --------------------------------------------------------
    //  Public API - data management
    // --------------------------------------------------------

    /// Replaces the entire list with new items. The list is copied.
    /// Clears pick state.
    public void UpdateData(List<T> items)
    {
        ArgumentNullException.ThrowIfNull(items);

        _items = [.. items];
        _pickedItem = default;
        _pickedIndex = -1;
        MarkRowHeightDirty();
        MarkRowsDirty();
    }

    /// Removes the first item matching the predicate from this control's copy;
    /// the list originally passed in is left alone.
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
            MarkRowsDirty();
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
            EnsureVisible(i);
            return true;
        }

        return false;
    }

    public void ClearPick()
    {
        _pickedItem = default;
        _pickedIndex = -1;
        MarkRowsDirty();
    }

    public void InvalidateRows() => MarkRowsDirty();

    private void MarkRowsDirty()
    {
        _rowsDirty = true;
        Invalidate();
    }

    /// <summary>Forces the probe row to be measured again. Call when the items change.</summary>
    private void MarkRowHeightDirty()
    {
        _rowHeight = -1;
        _measuredWidth = -1;
    }

    private void SetPicked(int index)
    {
        if (index == _pickedIndex)
            return;

        _pickedIndex = index;
        _pickedItem = index >= 0 && index < _items.Count ? _items[index] : default;
        MarkRowsDirty();
    }

    // --------------------------------------------------------
    //  Row management
    // --------------------------------------------------------

    private void RebuildRows()
    {
        ClearRows();

        for (var i = 0; i < _items.Count; i++)
            AppendRow(i);

        _windowStart = 0;
        _rowsDirty = false;
    }

    private void ClearRows()
    {
        foreach (var (_, component) in _rows)
            component.Dispose();

        _rows.Clear();
        _childrenView.Clear();
    }

    /// <summary>Builds the component for item <paramref name="index"/> and appends it.</summary>
    private IComponent AppendRow(int index)
    {
        var item = _items[index];
        var component = _rowFactory(item, index == _pickedIndex);

        component.Transient = true;
        component.Parent = this;
        component.Context = Context;

        if (component.VerticalSize != SizeMode.Fixed)
            component.VerticalSize = SizeMode.Shrink;

        _rows.Add((item, component));
        _childrenView.Add(component);

        return component;
    }

    /// <summary>
    /// Measures one row to learn the height every row is assumed to have.
    /// </summary>
    private void EnsureRowHeight(float width)
    {
        if (_rowHeight >= 0 && Math.Abs(_measuredWidth - width) < 0.5f)
            return;

        if (_items.Count == 0)
        {
            _rowHeight = 0;
            _rowWidth = 0;
            _measuredWidth = width;
            return;
        }

        var probe = _rowFactory(_items[0], false);
        probe.Transient = true;
        probe.Parent = this;
        probe.Context = Context;

        if (probe.VerticalSize != SizeMode.Fixed)
            probe.VerticalSize = SizeMode.Shrink;

        probe.Measure(new SKSize(width, float.MaxValue));

        _rowHeight = probe.DesiredSize.Height;
        _rowWidth = probe.DesiredSize.Width;
        _measuredWidth = width;

        probe.Dispose();
    }

    /// <summary>
    /// Rebuilds <see cref="_rows"/> so it covers the rows on screen, and measures and resolves
    /// them. Called from arrange, where the viewport is finally known.
    /// </summary>
    private void EnsureWindow(SKRect contentBounds)
    {
        EnsureRowHeight(contentBounds.Width);

        var pitch = RowPitch;

        var first = pitch <= 0 ? 0 : (int)(ScrollOffset / pitch) - Overscan;
        var last = pitch <= 0
            ? _items.Count - 1
            : (int)((ScrollOffset + contentBounds.Height) / pitch) + Overscan;

        first = Math.Max(0, first);
        last = Math.Min(_items.Count - 1, last);

        var count = Math.Max(0, last - first + 1);

        // Nothing moved and nothing changed: the rows already standing are the right ones.
        if (!_rowsDirty && _windowStart == first && _rows.Count == count)
            return;

        ClearRows();
        _windowStart = first;

        for (var i = first; i <= last; i++)
        {
            var component = AppendRow(i);
            component.Measure(new SKSize(contentBounds.Width, float.MaxValue));
            component.Resolve();
        }

        _rowsDirty = false;
    }

    /// <summary>Scrolls so that item <paramref name="index"/> is inside the viewport.</summary>
    public void EnsureVisible(int index)
    {
        if (index < 0 || index >= _items.Count || ViewportSize <= 0)
            return;

        if (!Virtualized)
        {
            // Unvirtualized rows know where they are only after an arrange; use what they report.
            if (index >= _rows.Count)
                return;

            var bounds = _rows[index].Component.ArrangedBounds;
            var top = bounds.Top - ContentTop + ScrollOffset;

            ScrollIntoView(top, bounds.Height);
            return;
        }

        var pitch = RowPitch;
        if (pitch <= 0)
            return;

        ScrollIntoView(index * pitch, _rowHeight);
    }

    /// <summary>Top of the content area in absolute coordinates, for translating arranged rows.</summary>
    private float ContentTop { get; set; }

    private void ScrollIntoView(float top, float height)
    {
        var bottom = top + height;

        if (top < ScrollOffset)
            ScrollTo(top);
        else if (bottom > ScrollOffset + ViewportSize)
            ScrollTo(bottom - ViewportSize);
    }

    // --------------------------------------------------------
    //  Measure - unconstrained height, track content size
    // --------------------------------------------------------

    protected override SKSize MeasureContent(SKSize availableSize)
    {
        // Virtualized: the total is arithmetic on the item count, so it costs the same for ten
        // rows as for ten thousand. The rows themselves are built during arrange, where the
        // viewport that decides which ones are needed is known.
        if (Virtualized)
        {
            EnsureRowHeight(availableSize.Width);
            ContentSize = VirtualContentSize;

            return new SKSize(_rowWidth, ContentSize);
        }

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
    //  Resolve - cascade into rows
    // --------------------------------------------------------
    //  Rows are laid out by this control, but a row is itself a
    //  container that may hold Flexible children, and those are
    //  only distributed during Resolve.
    // --------------------------------------------------------

    protected override void ResolveContent()
    {
        foreach (var (_, component) in _rows)
            component.Resolve();
    }

    // --------------------------------------------------------
    //  Arrange - sequential vertical with scroll offset
    // --------------------------------------------------------

    protected override void ArrangeContent(SKRect contentBounds)
    {
        ViewportSize = contentBounds.Height;
        ContentTop = contentBounds.Top;
        ScrollOffset = Math.Clamp(ScrollOffset, 0, ((IScrollable)this).MaxScroll);

        if (Virtualized)
            EnsureWindow(contentBounds);
        
        var yOffset = Virtualized ? _windowStart * RowPitch : 0f;
        var first = true;

        foreach (var (_, component) in _rows)
        {
            if (!first)
                yOffset += RowSpacing;

            first = false;

            var rowHeight = Virtualized ? _rowHeight : component.DesiredSize.Height;

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
                contentBounds.Top + yOffset - ScrollOffset + rowHeight)
            );

            yOffset += rowHeight;
        }

        // Publish the bar's hit region now, not at Draw - input arrives between frames.
        _scrollbar.UpdateLayout(contentBounds, this);
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

            if (_windowStart + i == _pickedIndex)
            {
                var highlightRect = new SKRect(
                    contentBounds.Left, component.ArrangedBounds.Top,
                    contentBounds.Right, component.ArrangedBounds.Bottom);

                using var highlightPaint = new SKPaint();
                highlightPaint.Color = PickedBackground;
                highlightPaint.IsAntialias = true;
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

            SetPicked(_windowStart + i);
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
            _childrenView.Clear();
        }

        base.Dispose(disposing);
    }
}