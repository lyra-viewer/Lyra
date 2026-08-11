using Lyra.UI.SupportingTypes;
using SkiaSharp;

namespace Lyra.UI.Components;

/// <summary>
/// Base contract for all LyraUI components.
///
/// Layout follows a four-phase pipeline:
///   1. Measure  (bottom-up)  - each component reports its desired size
///   2. Resolve  (top-down)   - containers distribute space to Flexible children
///   3. Arrange  (top-down)   - each component receives its final bounds
///   4. Render   (top-down)   - each component draws to the canvas
///
/// Components participate in a tree rooted at a single IComponent,
/// with IContainer extending this interface for parent nodes.
/// </summary>
public interface IComponent : IDisposable
{
    // --------------------------------------------------------
    //  Tree
    // --------------------------------------------------------

    /// <summary>
    /// The container that owns this component in the layout tree.
    /// Set by the parent when the component is added as a child.
    /// </summary>
    IContainer? Parent { get; set; }

    /// <summary>
    /// The context this component is displayed in, or null while detached.
    /// Set by the framework on attach and pushed down the subtree; components
    /// use it to mark the UI dirty when they change.
    /// </summary>
    UIContext? Context { get; set; }

    // --------------------------------------------------------
    //  Sizing
    // --------------------------------------------------------

    /// <summary>
    /// Controls how width is resolved during Measure.
    ///   Shrink   - fit content (default)
    ///   Expand   - fill available space from parent
    ///   Fixed    - use explicit Width value
    ///   Flexible - shrink to content, but accept resizing during Resolve
    /// </summary>
    SizeMode HorizontalSize { get; set; }

    /// <summary>
    /// Controls how height is resolved during Measure.
    ///   Shrink   - fit content (default)
    ///   Expand   - fill available space from parent
    ///   Fixed    - use explicit Height value
    ///   Flexible - shrink to content, but accept resizing during Resolve
    /// </summary>
    SizeMode VerticalSize { get; set; }

    /// <summary>
    /// Explicit width — used when HorizontalSize is Fixed.
    /// </summary>
    float? Width { get; set; }

    /// <summary>
    /// Explicit height — used when VerticalSize is Fixed.
    /// </summary>
    float? Height { get; set; }

    /// <summary>
    /// Minimum width constraint, applied after size mode resolution.
    /// </summary>
    float? MinWidth { get; set; }

    /// <summary>
    /// Maximum width constraint, applied after size mode resolution.
    /// Also clamps available space before MeasureContent.
    /// </summary>
    float? MaxWidth { get; set; }

    /// <summary>
    /// Minimum height constraint, applied after size mode resolution.
    /// </summary>
    float? MinHeight { get; set; }

    /// <summary>
    /// Maximum height constraint, applied after size mode resolution.
    /// Also clamps available space before MeasureContent.
    /// </summary>
    float? MaxHeight { get; set; }

    /// <summary>
    /// Insets between ArrangedBounds and the content area.
    /// Deflated before MeasureContent and inflated after.
    /// </summary>
    Padding Padding { get; set; }
    
    /// <summary>
    /// Which edges of this component can be user-resized.
    /// </summary>
    ResizeEdge ResizeEdges { get; set; }

    // --------------------------------------------------------
    //  Alignment
    // --------------------------------------------------------

    /// <summary>
    /// Horizontal position within the parent's cross axis.
    /// </summary>
    HAlign HorizontalAlign { get; set; }

    /// <summary>
    /// Vertical position within the parent's cross axis.
    /// </summary>
    VAlign VerticalAlign { get; set; }

    // --------------------------------------------------------
    //  Presence
    // --------------------------------------------------------
    //
    //  Three independent tiers, each assuming the previous is true:
    //
    //  Present -> false: excluded from layout entirely.
    //             Checked by the parent container.
    //
    //  Visible -> false: measured and arranged, but not rendered.
    //             Checked inside ComponentBase.Render.
    //
    //  Enabled -> false: rendered dimmed, input ignored.
    //             Checked inside ComponentBase.Render and input methods.
    //
    //  Visible and Enabled cascade through the parent chain
    //  via IsEffectivelyVisible / IsEffectivelyEnabled.
    //  Present cascades structurally - a non-present parent
    //  is never iterated, so its children are implicitly excluded.
    // --------------------------------------------------------

    /// <summary>
    /// When false, the component is excluded from layout entirely.
    /// The parent container skips it during Measure and Arrange.
    /// </summary>
    bool Present { get; set; }

    /// <summary>
    /// When false, the component is measured and arranged but not rendered.
    /// Cascades to children via IsEffectivelyVisible.
    /// </summary>
    bool Visible { get; set; }

    /// <summary>
    /// When false, the component is rendered dimmed and ignores input.
    /// Cascades to children via IsEffectivelyEnabled.
    /// </summary>
    bool Enabled { get; set; }

    /// <summary>
    /// True when this component and all ancestors are Visible.
    /// </summary>
    bool IsEffectivelyVisible { get; }

    /// <summary>
    /// True when this component and all ancestors are Enabled.
    /// </summary>
    bool IsEffectivelyEnabled { get; }

    // --------------------------------------------------------
    // Visuals
    // --------------------------------------------------------

    /// <summary>
    /// Background fill rendered at ArrangedBounds before content.
    /// Null means no background is drawn.
    /// </summary>
    SKColor? BackgroundColor { get; set; }

    // --------------------------------------------------------
    //  Hit-testing
    // --------------------------------------------------------

    /// <summary>
    /// When true, hit-testing skips this component and
    /// pointer events pass through to the component behind it.
    /// Used for internal children (e.g. Label/Image inside Button).
    /// </summary>
    bool Transient { get; set; }

    // --------------------------------------------------------
    //  Layout state
    // --------------------------------------------------------

    /// <summary>
    /// Size reported by the most recent Measure pass.
    /// Includes padding.
    /// </summary>
    SKSize DesiredSize { get; }

    /// <summary>
    /// Final bounds assigned by the parent during Arrange.
    /// </summary>
    SKRect ArrangedBounds { get; set; }

    // --------------------------------------------------------
    //  Layout pipeline
    // --------------------------------------------------------

    /// <summary>
    /// Bottom-up: compute DesiredSize given available space from the parent.
    /// </summary>
    SKSize Measure(SKSize availableSize);

    /// <summary>
    /// Top-down pass between Measure and Arrange.
    /// Containers distribute remaining space among Flexible children.
    /// </summary>
    void Resolve();

    /// <summary>
    /// Top-down: receive final bounds from parent and position children.
    /// </summary>
    void Arrange(SKRect finalBounds);

    /// <summary>
    /// Top-down: draw to the canvas. Runs every frame.
    /// </summary>
    void Render(SKCanvas canvas);

    // --------------------------------------------------------
    //  Input — default no-op
    // --------------------------------------------------------

    void OnPointerDown(SKPoint point) { }
    void OnPointerUp(SKPoint point) { }
    void OnPointerMove(SKPoint point) { }
    void OnPointerEnter() { }
    void OnPointerLeave() { }
}