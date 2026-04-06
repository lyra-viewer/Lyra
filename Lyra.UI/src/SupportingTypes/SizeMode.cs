namespace Lyra.UI.SupportingTypes;

/// <summary>
/// Controls how a component's width or height is resolved during Measure.
/// </summary>
public enum SizeMode
{
    /// <summary>
    /// Size to fit content. The component's dimension equals
    /// what MeasureContent reports.
    /// </summary>
    Shrink,

    /// <summary>
    /// Fill all available space from the parent.
    /// </summary>
    Expand,

    /// <summary>
    /// Use the explicit Width/Height value.
    /// Falls back to content size if not set.
    /// </summary>
    Fixed,

    /// <summary>
    /// Initially measures like Shrink, but the parent container
    /// may redistribute remaining space during the Resolve pass.
    /// Capped at available space - never exceeds the parent's offer.
    /// </summary>
    Flexible
}