using Lyra.UI.Components.Primitives;
using Lyra.UI.SupportingTypes;
using Lyra.UI.Theme;
using SkiaSharp;

namespace Lyra.UI.Components.Controls.TreeView;

// ============================================================================
//  TreeView<T> - generic tree component with built-in scrolling
// ----------------------------------------------------------------------------
//  Renders a tree of TreeNode<T> as a flat list of indented rows
//  with integrated viewport clipping, scroll offset, and scrollbar.
//
//  Row visuals are produced by a caller-supplied factory delegate.
//
//  Responsibilities:
//    - Flattening visible nodes (expanded only) into rows
//    - Indentation per depth level
//    - Expand/collapse arrows for non-leaf nodes (or custom icons
//      via IconProvider)
//    - Single-item pick with visual highlight
//    - Bidirectional pick: click fires Picked, Locate() programmatically
//    - Built-in IScrollable - no VScrollContainer wrapper needed
//
//  Row components from the factory are set to Transient = true
//  so hit-testing lands on TreeView itself. TreeView then maps
//  the click position to a row and handles pick/expand.
// ============================================================================
public class TreeView<T> : ComponentBase, IContainer, IScrollable
{
    private readonly Func<TreeNode<T>, bool, IComponent> _rowFactory;
    private List<TreeNode<T>> _roots;

    private readonly List<(TreeNode<T> Node, IComponent Component)> _rows = [];
    private bool _rowsDirty = true;
    
    private readonly List<IComponent> _childrenView = [];
    private bool _scrollToPickedPending;

    // --------------------------------------------------------
    //  Pick state
    // --------------------------------------------------------

    private TreeNode<T>? _pickedNode;

    public TreeNode<T>? PickedNode
    {
        get => _pickedNode;
        private set
        {
            if (ReferenceEquals(_pickedNode, value))
                return;

            _pickedNode = value;
            MarkRowsDirty();
        }
    }

    /// Fired when a row is picked by click. Delivers the node itself
    /// so subscribers can inspect its subtree (e.g. to jump to the
    /// closest descendant that holds content).
    public event Action<TreeNode<T>>? Picked;

    // --------------------------------------------------------
    //  Visual settings
    // --------------------------------------------------------

    /// Horizontal indent per tree depth level.
    public float IndentWidth { get; set; } = 20f;

    /// Vertical spacing between rows.
    public float RowSpacing { get; set; }

    /// Width of the expand/collapse arrow column.
    public float ArrowColumnWidth { get; set; } = 20f;

    /// Size of the arrow triangle.
    public float ArrowSize { get; set; } = 8f;

    /// Background color for the picked row.
    public SKColor PickedBackground { get; set; } = Palette.SelectedBackground;

    /// Arrow color.
    public SKColor ArrowColor { get; set; } = Palette.Muted;

    /// Optional icon provider for the indicator column.
    /// When set and returning non-null, the icon replaces the
    /// default expand/collapse arrow for that node.
    public Func<TreeNode<T>, SvgImage?>? IconProvider { get; set; }

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

    /// <param name="roots">The list is copied; the nodes themselves are not. Node
    /// objects stay shared on purpose - TreeNode is the data model and carries
    /// expand state - but reordering or adding to the caller's list no longer
    /// desyncs the rows. Call UpdateData to publish structural changes.</param>
    public TreeView(List<TreeNode<T>> roots, Func<TreeNode<T>, bool, IComponent> rowFactory)
    {
        ArgumentNullException.ThrowIfNull(roots);
        ArgumentNullException.ThrowIfNull(rowFactory);

        _roots = [..roots];
        _rowFactory = rowFactory;
    }

    // --------------------------------------------------------
    //  IContainer
    // --------------------------------------------------------

    public IReadOnlyList<IComponent> Children => _childrenView;

    public void AddComponent(IComponent child) =>
        throw new NotSupportedException("TreeView children are managed by the tree data model. Use TreeNode<T>.AddChild or UpdateData instead.");

    public void AddComponents(params IComponent[] children) =>
        throw new NotSupportedException("TreeView children are managed by the tree data model. Use TreeNode<T>.AddChild or UpdateData instead.");

    // --------------------------------------------------------
    //  Public API - data management
    // --------------------------------------------------------

    /// Replaces the entire tree with new roots. The root list is copied;
    /// the nodes are not. Clears pick state.
    public void UpdateData(List<TreeNode<T>> roots)
    {
        ArgumentNullException.ThrowIfNull(roots);

        _roots = [..roots];
        _pickedNode = null;
        MarkRowsDirty();
    }

    /// Removes the first node matching the predicate. A root is removed from this
    /// control's copy of the root list; a nested node is removed from its parent's
    /// Children, which is shared with the caller.
    /// Clears pick if the removed node was picked.
    /// Returns true if a node was removed.
    public bool Remove(Func<T, bool> predicate)
    {
        var removed = RemoveFromList(_roots, predicate);

        if (removed && _pickedNode != null && predicate(_pickedNode.Data))
            _pickedNode = null;

        if (removed)
            MarkRowsDirty();

        return removed;
    }

    private static bool RemoveFromList(List<TreeNode<T>> nodes, Func<T, bool> predicate)
    {
        for (var i = 0; i < nodes.Count; i++)
        {
            if (predicate(nodes[i].Data))
            {
                nodes.RemoveAt(i);
                return true;
            }

            if (RemoveFromList(nodes[i].Children, predicate))
                return true;
        }

        return false;
    }

    // --------------------------------------------------------
    //  Public API - navigation
    // --------------------------------------------------------

    /// Finds a node matching the predicate, expands its ancestors
    /// so it becomes visible, and picks it.
    /// Returns true if a matching node was found.
    public bool Locate(Func<T, bool> predicate)
    {
        foreach (var root in _roots)
        {
            var found = root.Find(predicate);
            if (found != null)
            {
                found.ExpandToRoot();
                PickedNode = found;
                _scrollToPickedPending = true;
                return true;
            }
        }

        return false;
    }

    /// Clears the current pick.
    public void ClearPick()
    {
        PickedNode = null;
    }

    /// Marks rows for rebuild.
    public void InvalidateRows() => MarkRowsDirty();

    private void MarkRowsDirty()
    {
        _rowsDirty = true;
        Invalidate();
    }

    // --------------------------------------------------------
    //  Row management
    // --------------------------------------------------------

    private void RebuildRows()
    {
        foreach (var (_, component) in _rows)
            component.Dispose();

        _rows.Clear();
        _childrenView.Clear();

        foreach (var root in _roots)
        {
            foreach (var node in root.FlattenVisible())
            {
                var isPicked = ReferenceEquals(node, _pickedNode);
                var component = _rowFactory(node, isPicked);

                component.Transient = true;
                component.Parent = this;
                component.Context = Context;

                _rows.Add((node, component));
                _childrenView.Add(component);
            }
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

        foreach (var (node, component) in _rows)
        {
            if (!first)
                totalHeight += RowSpacing;
            
            first = false;

            var indent = node.Depth * IndentWidth + ArrowColumnWidth;
            var contentWidth = Math.Max(0, availableSize.Width - indent);

            component.Measure(new SKSize(contentWidth, float.MaxValue));

            totalHeight += component.DesiredSize.Height;
            maxWidth = Math.Max(maxWidth, indent + component.DesiredSize.Width);
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
    //  Arrange - indented rows with scroll offset
    // --------------------------------------------------------

    protected override void ArrangeContent(SKRect contentBounds)
    {
        ViewportSize = contentBounds.Height;

        // Scroll to make the picked node visible after Locate()
        if (_scrollToPickedPending && _pickedNode != null)
        {
            _scrollToPickedPending = false;
            ScrollToNodeIfNeeded(_pickedNode, contentBounds.Height);
        }

        ScrollOffset = Math.Clamp(ScrollOffset, 0, ((IScrollable)this).MaxScroll);

        var yOffset = 0f;
        var first = true;

        foreach (var (node, component) in _rows)
        {
            if (!first)
                yOffset += RowSpacing;
            
            first = false;

            var indent = node.Depth * IndentWidth + ArrowColumnWidth;
            var rowHeight = component.DesiredSize.Height;

            var contentWidth = component.HorizontalSize == SizeMode.Expand
                ? contentBounds.Width - indent
                : component.DesiredSize.Width;

            component.Arrange(new SKRect(
                contentBounds.Left + indent,
                contentBounds.Top + yOffset - ScrollOffset,
                contentBounds.Left + indent + contentWidth,
                contentBounds.Top + yOffset - ScrollOffset + rowHeight)
            );

            yOffset += rowHeight;
        }

        // Publish the bar's hit region now, not at Draw - input arrives between frames.
        _scrollbar.UpdateLayout(contentBounds, this);
    }

    /// <summary>
    /// Adjusts ScrollOffset so the target node is visible within the viewport.
    /// Uses measured row heights (Measure runs before Arrange).
    /// </summary>
    private void ScrollToNodeIfNeeded(TreeNode<T> target, float viewportHeight)
    {
        var yOffset = 0f;
        var first = true;

        foreach (var (node, component) in _rows)
        {
            if (!first)
                yOffset += RowSpacing;
            
            first = false;

            var rowHeight = component.DesiredSize.Height;

            if (ReferenceEquals(node, target))
            {
                var rowTop = yOffset;
                var rowBottom = yOffset + rowHeight;

                // Already visible - no adjustment
                if (rowTop >= ScrollOffset && rowBottom <= ScrollOffset + viewportHeight)
                    return;

                // Above viewport - scroll up so the row is at the top
                if (rowTop < ScrollOffset)
                {
                    ScrollOffset = rowTop;
                    return;
                }

                // Below viewport - scroll down so the row is at the bottom
                ScrollOffset = rowBottom - viewportHeight;
                return;
            }

            yOffset += rowHeight;
        }
    }

    // --------------------------------------------------------
    //  Render - clipped rows + icons/arrows + pick highlight + scrollbar
    // --------------------------------------------------------

    protected override void RenderContent(SKCanvas canvas, SKRect contentBounds)
    {
        canvas.Save();
        canvas.ClipRect(contentBounds);

        foreach (var (node, component) in _rows)
        {
            // Skip rows entirely outside viewport.
            if (component.ArrangedBounds.Bottom < contentBounds.Top ||
                component.ArrangedBounds.Top > contentBounds.Bottom)
                continue;

            var rowTop = component.ArrangedBounds.Top;
            var rowBottom = component.ArrangedBounds.Bottom;
            var rowHeight = rowBottom - rowTop;

            // Pick highlight - full width.
            if (ReferenceEquals(node, _pickedNode))
            {
                var highlightRect = new SKRect(
                    contentBounds.Left, rowTop,
                    contentBounds.Right, rowBottom);

                using var highlightPaint = new SKPaint();
                highlightPaint.Color = PickedBackground;
                highlightPaint.IsAntialias = true;
                canvas.DrawRect(highlightRect, highlightPaint);
            }

            // Indicator column for non-leaf nodes: icon or arrow.
            if (!node.IsLeaf)
            {
                var indent = node.Depth * IndentWidth;
                var arrowCenterX = contentBounds.Left + indent + ArrowColumnWidth / 2f;
                var arrowCenterY = rowTop + rowHeight / 2f;

                var icon = IconProvider?.Invoke(node);
                if (icon != null)
                    DrawIcon(canvas, icon, arrowCenterX, arrowCenterY, ArrowSize);
                else
                    DrawArrow(canvas, arrowCenterX, arrowCenterY, node.IsExpanded);
            }

            component.Render(canvas);
        }

        canvas.Restore();

        _scrollbar.Draw(canvas, contentBounds, this);
    }

    private void DrawArrow(SKCanvas canvas, float cx, float cy, bool expanded)
    {
        var half = ArrowSize / 2f;

        using var paint = new SKPaint();
        paint.Color = ArrowColor;
        paint.IsAntialias = true;
        paint.Style = SKPaintStyle.Fill;

        using var builder = new SKPathBuilder();

        if (expanded)
        {
            // Down-pointing triangle ▼
            builder.MoveTo(cx - half, cy - half * 0.5f);
            builder.LineTo(cx + half, cy - half * 0.5f);
            builder.LineTo(cx, cy + half * 0.5f);
        }
        else
        {
            // Right-pointing triangle ▶
            builder.MoveTo(cx - half * 0.4f, cy - half);
            builder.LineTo(cx + half * 0.6f, cy);
            builder.LineTo(cx - half * 0.4f, cy + half);
        }

        builder.Close();
        using var path = builder.Detach();
        canvas.DrawPath(path, paint);
    }

    private static void DrawIcon(SKCanvas canvas, SvgImage icon, float cx, float cy, float halfSize)
    {
        var size = halfSize * 2;
        icon.Measure(new SKSize(size, size));
        icon.Arrange(new SKRect(cx - halfSize, cy - halfSize, cx + halfSize, cy + halfSize));
        icon.Render(canvas);
    }

    // --------------------------------------------------------
    //  Input - click to pick or expand/collapse
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

        foreach (var (node, component) in _rows)
        {
            var rowTop = component.ArrangedBounds.Top;
            var rowBottom = component.ArrangedBounds.Bottom;

            if (point.Y < rowTop || point.Y > rowBottom)
                continue;

            // Check if click was in the arrow column.
            var indent = node.Depth * IndentWidth;
            var arrowRight = ArrangedBounds.Left + Padding.Left + indent + ArrowColumnWidth;

            if (!node.IsLeaf && point.X < arrowRight)
            {
                node.IsExpanded = !node.IsExpanded;
                MarkRowsDirty();
            }
            else
            {
                PickedNode = node;
                Picked?.Invoke(node);
            }

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