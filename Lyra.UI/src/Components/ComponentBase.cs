using Lyra.UI.SupportingTypes;
using Lyra.UI.Theme;
using SkiaSharp;

namespace Lyra.UI.Components;

/// <summary>
/// Base implementation of IComponent.
///
/// Centralizes the box model, presence logic, and rendering pipeline.
/// Subclasses override three content-space methods:
///   MeasureContent  - return intrinsic size (no padding)
///   ArrangeContent  - position children within content bounds
///   RenderContent   - draw visuals within content bounds
///
/// Box model (outside -> inside):
///   ArrangedBounds  - set by parent
///   > Background    - fills ArrangedBounds
///   >>> Padding     - deflates ArrangedBounds to content bounds
///   >>>>> Content   - subclass domain
/// </summary>
public abstract class ComponentBase : IComponent
{
    // --------------------------------------------------------
    //  Tree
    // --------------------------------------------------------

    private IContainer? _parent;
    private UIContext? _context;

    public IContainer? Parent
    {
        get => _parent;
        set
        {
            _parent = value;
            
            if (value?.Context is { } inherited)
                Context = inherited;
        }
    }

    /// <summary>
    /// The context this component is displayed in, or null while detached.
    ///
    /// Set by the framework on attach - assigning a Layer's Root, or adding a
    /// child to a container that already has one - and pushed down the subtree,
    /// so a component built before attachment still ends up connected.
    ///
    /// This is what lets a component mark the UI dirty when it changes. Without
    /// it every mutation depended on the caller remembering to invalidate, which
    /// only held because input handling invalidated unconditionally.
    /// </summary>
    public UIContext? Context
    {
        get => _context;
        set
        {
            if (ReferenceEquals(_context, value))
                return;

            _context = value;
            PropagateContext(value);
        }
    }

    /// <summary>
    /// Pushes the context to everything this component owns.
    ///
    /// The base covers IContainer.Children. Controls that own components outside
    /// that list - a Button's Content, a Collapsible's internal stack - must
    /// override and forward to them as well, or those parts stay detached and
    /// silently cannot invalidate.
    /// </summary>
    protected virtual void PropagateContext(UIContext? context)
    {
        if (this is not IContainer container)
            return;

        foreach (var child in container.Children)
            child.Context = context;
    }

    /// <summary>
    /// Marks the UI as needing a new layout and paint. A no-op while detached.
    /// </summary>
    protected void Invalidate() => _context?.Invalidate();

    /// <summary>
    /// Assigns a property and invalidates, but only when the value actually
    /// changes.
    ///
    /// The equality guard is the point. Refresh code writes the same values back
    /// every frame - the same label text, the same Present flag - and without a
    /// guard each of those writes would dirty the layout and force a full
    /// re-measure of every layer for no reason.
    /// </summary>
    protected void Set<TValue>(ref TValue field, TValue value)
    {
        if (EqualityComparer<TValue>.Default.Equals(field, value))
            return;

        field = value;
        Invalidate();
    }

    // --------------------------------------------------------
    //  Sizing
    // --------------------------------------------------------

    private SizeMode _horizontalSize = SizeMode.Shrink;
    private SizeMode _verticalSize = SizeMode.Shrink;
    private float? _width;
    private float? _height;
    private float? _minWidth;
    private float? _maxWidth;
    private float? _minHeight;
    private float? _maxHeight;
    private Padding _padding;
    private ResizeEdge _resizeEdges = ResizeEdge.None;

    public SizeMode HorizontalSize
    {
        get => _horizontalSize;
        set => Set(ref _horizontalSize, value);
    }

    public SizeMode VerticalSize
    {
        get => _verticalSize;
        set => Set(ref _verticalSize, value);
    }

    public float? Width
    {
        get => _width;
        set => Set(ref _width, value);
    }

    public float? Height
    {
        get => _height;
        set => Set(ref _height, value);
    }

    public float? MinWidth
    {
        get => _minWidth;
        set => Set(ref _minWidth, value);
    }

    public float? MaxWidth
    {
        get => _maxWidth;
        set => Set(ref _maxWidth, value);
    }

    public float? MinHeight
    {
        get => _minHeight;
        set => Set(ref _minHeight, value);
    }

    public float? MaxHeight
    {
        get => _maxHeight;
        set => Set(ref _maxHeight, value);
    }

    public Padding Padding
    {
        get => _padding;
        set => Set(ref _padding, value);
    }

    public ResizeEdge ResizeEdges
    {
        get => _resizeEdges;
        set => Set(ref _resizeEdges, value);
    }

    // --------------------------------------------------------
    //  Alignment
    // --------------------------------------------------------

    private HAlign _horizontalAlign = HAlign.Left;
    private VAlign _verticalAlign = VAlign.Top;

    public HAlign HorizontalAlign
    {
        get => _horizontalAlign;
        set => Set(ref _horizontalAlign, value);
    }

    public VAlign VerticalAlign
    {
        get => _verticalAlign;
        set => Set(ref _verticalAlign, value);
    }

    // --------------------------------------------------------
    //  Presence
    // --------------------------------------------------------

    private bool _present = true;
    private bool _visible = true;
    private bool _enabled = true;

    public bool Present
    {
        get => _present;
        set => Set(ref _present, value);
    }

    public bool Visible
    {
        get => _visible;
        set => Set(ref _visible, value);
    }

    public bool Enabled
    {
        get => _enabled;
        set => Set(ref _enabled, value);
    }

    public bool IsEffectivelyVisible
    {
        get
        {
            if (!Visible) return false;
            return Parent is not ComponentBase parent || parent.IsEffectivelyVisible;
        }
    }

    public bool IsEffectivelyEnabled
    {
        get
        {
            if (!Enabled) return false;
            return Parent is not ComponentBase parent || parent.IsEffectivelyEnabled;
        }
    }

    // --------------------------------------------------------
    //  Visuals
    // --------------------------------------------------------

    private SKColor? _backgroundColor;

    /// <summary>
    /// Background fill rendered at ArrangedBounds before content.
    /// Null means no background is drawn (the default).
    /// </summary>
    public SKColor? BackgroundColor
    {
        get => _backgroundColor;
        set => Set(ref _backgroundColor, value);
    }

    // --------------------------------------------------------
    //  Hit-testing
    // --------------------------------------------------------

    private bool _transient;

    public bool Transient
    {
        get => _transient;
        set => Set(ref _transient, value);
    }

    // --------------------------------------------------------
    //  Layout state
    // --------------------------------------------------------

    public SKSize DesiredSize { get; protected set; }
    public SKRect ArrangedBounds { get; set; }

    /// <summary>
    /// ArrangedBounds deflated by Padding.
    /// Computed once during Arrange and reused in Render.
    /// </summary>
    protected SKRect ContentBounds { get; private set; }

    // --------------------------------------------------------
    //  Subclass overrides — work in content space (no padding)
    // --------------------------------------------------------

    protected abstract SKSize MeasureContent(SKSize availableSize);
    protected abstract void ArrangeContent(SKRect contentBounds);
    protected abstract void RenderContent(SKCanvas canvas, SKRect contentBounds);

    // --------------------------------------------------------
    //  Measure
    // --------------------------------------------------------
    //  1. Deflate by padding
    //  2. Clamp available space by max constraints
    //  3. Subclass measures content
    //  4. Apply size mode
    //  5. Clamp to min/max constraints
    //  6. Add padding back
    // --------------------------------------------------------

    public SKSize Measure(SKSize availableSize)
    {
        var contentAvailable = new SKSize(
            Math.Max(0, availableSize.Width - Padding.Horizontal),
            Math.Max(0, availableSize.Height - Padding.Vertical));

        if (MaxWidth.HasValue)
            contentAvailable = contentAvailable with { Width = Math.Min(contentAvailable.Width, MaxWidth.Value) };

        if (MaxHeight.HasValue)
            contentAvailable = contentAvailable with { Height = Math.Min(contentAvailable.Height, MaxHeight.Value) };

        if (HorizontalSize == SizeMode.Fixed && Width.HasValue)
            contentAvailable = contentAvailable with { Width = Math.Min(contentAvailable.Width, Width.Value) };

        if (VerticalSize == SizeMode.Fixed && Height.HasValue)
            contentAvailable = contentAvailable with { Height = Math.Min(contentAvailable.Height, Height.Value) };

        var contentSize = MeasureContent(contentAvailable);

        var width = HorizontalSize switch
        {
            SizeMode.Expand => contentAvailable.Width,
            SizeMode.Fixed => Width ?? contentSize.Width,
            SizeMode.Flexible => Math.Min(contentSize.Width, contentAvailable.Width),
            _ => contentSize.Width
        };

        var height = VerticalSize switch
        {
            SizeMode.Expand => contentAvailable.Height,
            SizeMode.Fixed => Height ?? contentSize.Height,
            SizeMode.Flexible => Math.Min(contentSize.Height, contentAvailable.Height),
            _ => contentSize.Height
        };

        if (MinWidth.HasValue)
            width = Math.Max(width, MinWidth.Value);

        if (MaxWidth.HasValue)
            width = Math.Min(width, MaxWidth.Value);

        if (MinHeight.HasValue)
            height = Math.Max(height, MinHeight.Value);

        if (MaxHeight.HasValue)
            height = Math.Min(height, MaxHeight.Value);

        DesiredSize = new SKSize(width + Padding.Horizontal, height + Padding.Vertical);

        return DesiredSize;
    }

    // --------------------------------------------------------
    //  Resolve
    // --------------------------------------------------------
    //  Top-down pass between Measure and Arrange.
    //  Containers override ResolveContent to distribute space
    //  among Flexible children and re-measure them at adjusted
    //  sizes. Leaf components do nothing.
    // --------------------------------------------------------

    public void Resolve()
    {
        if (!Present)
            return;

        ResolveContent();
    }

    protected virtual void ResolveContent() { }

    // --------------------------------------------------------
    //  Arrange
    // --------------------------------------------------------

    public void Arrange(SKRect finalBounds)
    {
        ArrangedBounds = finalBounds;
        ContentBounds = DeflateByPadding(finalBounds);
        ArrangeContent(ContentBounds);
    }

    // --------------------------------------------------------
    //  Render
    // --------------------------------------------------------
    //  Order: visibility check -> background -> disabled wrapper -> content
    // --------------------------------------------------------

    private static readonly SKPaint DisabledPaint = new() { Color = Palette.Disabled };

    public void Render(SKCanvas canvas)
    {
        if (!IsEffectivelyVisible)
            return;

        if (BackgroundColor.HasValue)
        {
            using var bgPaint = new SKPaint();
            bgPaint.Color = BackgroundColor.Value;
            canvas.DrawRect(ArrangedBounds, bgPaint);
        }

        if (!IsEffectivelyEnabled)
        {
            canvas.SaveLayer(DisabledPaint);
            RenderContent(canvas, ContentBounds);
            canvas.Restore();
            return;
        }

        RenderContent(canvas, ContentBounds);
    }

    // --------------------------------------------------------
    //  Input
    // --------------------------------------------------------

    public Action<SKPoint>? PointerDown { get; set; }
    public Action<SKPoint>? PointerUp { get; set; }
    public Action<SKPoint>? PointerMove { get; set; }
    public Action? PointerEnter { get; set; }
    public Action? PointerLeave { get; set; }

    public void OnPointerDown(SKPoint point)
    {
        OnPointerDownCore(point);

        if (IsEffectivelyEnabled)
            PointerDown?.Invoke(point);
    }

    public void OnPointerUp(SKPoint point)
    {
        OnPointerUpCore(point);

        if (IsEffectivelyEnabled)
            PointerUp?.Invoke(point);
    }

    public void OnPointerMove(SKPoint point)
    {
        OnPointerMoveCore(point);

        if (IsEffectivelyEnabled)
            PointerMove?.Invoke(point);
    }

    public void OnPointerEnter()
    {
        OnPointerEnterCore();

        if (IsEffectivelyEnabled)
            PointerEnter?.Invoke();
    }

    public void OnPointerLeave()
    {
        OnPointerLeaveCore();
        PointerLeave?.Invoke();
    }

    protected virtual void OnPointerDownCore(SKPoint point) { }
    protected virtual void OnPointerUpCore(SKPoint point) { }
    protected virtual void OnPointerMoveCore(SKPoint point) { }
    protected virtual void OnPointerEnterCore() { }
    protected virtual void OnPointerLeaveCore() { }

    // --------------------------------------------------------
    //  Dispose
    // --------------------------------------------------------
    //
    //  Convention:
    //    - Dispose resources this component CREATED
    //    - Dispose CHILDREN owned by this component
    //    - Do NOT dispose external references (Source, callbacks)
    // --------------------------------------------------------

    protected virtual void Dispose(bool disposing) { }

    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }

    // --------------------------------------------------------
    //  Helpers
    // --------------------------------------------------------

    private SKRect DeflateByPadding(SKRect bounds) => new(
        bounds.Left + Padding.Left,
        bounds.Top + Padding.Top,
        bounds.Right - Padding.Right,
        bounds.Bottom - Padding.Bottom);
}