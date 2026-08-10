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

    public IContainer? Parent { get; set; }

    // --------------------------------------------------------
    //  Sizing
    // --------------------------------------------------------

    public SizeMode HorizontalSize { get; set; } = SizeMode.Shrink;
    public SizeMode VerticalSize { get; set; } = SizeMode.Shrink;

    public float? Width { get; set; }
    public float? Height { get; set; }

    public float? MinWidth { get; set; }
    public float? MaxWidth { get; set; }
    public float? MinHeight { get; set; }
    public float? MaxHeight { get; set; }

    public Padding Padding { get; set; }
    
    public ResizeEdge ResizeEdges { get; set; } = ResizeEdge.None;

    // --------------------------------------------------------
    //  Alignment
    // --------------------------------------------------------

    public HAlign HorizontalAlign { get; set; } = HAlign.Left;
    public VAlign VerticalAlign { get; set; } = VAlign.Top;

    // --------------------------------------------------------
    //  Presence
    // --------------------------------------------------------

    public bool Present { get; set; } = true;
    public bool Visible { get; set; } = true;
    public bool Enabled { get; set; } = true;

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

    /// <summary>
    /// Background fill rendered at ArrangedBounds before content.
    /// Null means no background is drawn (the default).
    /// </summary>
    public SKColor? BackgroundColor { get; set; }

    // --------------------------------------------------------
    //  Hit-testing
    // --------------------------------------------------------

    public bool Transient { get; set; }

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
            using var bgPaint = new SKPaint { Color = BackgroundColor.Value };
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