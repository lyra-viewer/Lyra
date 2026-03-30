using Lyra.UI.SupportingTypes;
using SkiaSharp;

namespace Lyra.UI.Components;

/// <summary>
/// Base contract for all LyraUI components.
///
/// Layout follows a three-phase pipeline:
/// - Measure (bottom-up)
/// - Arrange (top-down)
/// - Render (top-down)
///
/// Components participate in a tree rooted at a single IComponent,
/// with IContainer extending this interface for parent nodes.
/// </summary>
public interface IComponent : IDisposable
{
    // --------------------------------------------------------
    //  Tree
    // --------------------------------------------------------

    IContainer? Parent { get; set; }

    // --------------------------------------------------------
    //  Sizing
    // --------------------------------------------------------

    /// Shrink = fit content, Expand = fill parent, Fixed = use Width/Height.
    SizeMode HorizontalSize { get; set; }
    SizeMode VerticalSize { get; set; }

    /// Explicit dimensions — used when SizeMode is Fixed.
    float? Width { get; set; }
    float? Height { get; set; }

    /// Constraints — clamped after size mode resolution.
    float? MinWidth { get; set; }
    float? MaxWidth { get; set; }
    float? MinHeight { get; set; }
    float? MaxHeight { get; set; }

    Padding Padding { get; set; }

    // --------------------------------------------------------
    //  Alignment
    // --------------------------------------------------------

    /// Position within the parent's cross axis.
    HAlign HorizontalAlign { get; set; }
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

    bool Present { get; set; }
    bool Visible { get; set; }
    bool Enabled { get; set; }

    bool IsEffectivelyVisible { get; }
    bool IsEffectivelyEnabled { get; }

    // --------------------------------------------------------
    // Visuals
    // --------------------------------------------------------

    /// Background fill rendered at ArrangedBounds before content.
    SKColor? BackgroundColor { get; set; }

    // --------------------------------------------------------
    //  Hit-testing
    // --------------------------------------------------------

    /// When true, hit-testing skips this component and
    /// pointer events pass through to the component behind it.
    bool Transient { get; set; }

    // --------------------------------------------------------
    //  Layout state
    // --------------------------------------------------------

    SKSize DesiredSize { get; }
    SKRect ArrangedBounds { get; set; }

    // --------------------------------------------------------
    //  Layout pipeline
    // --------------------------------------------------------

    SKSize Measure(SKSize availableSize);
    void Arrange(SKRect finalBounds);
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