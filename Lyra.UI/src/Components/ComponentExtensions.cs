using Lyra.UI.SupportingTypes;
using SkiaSharp;

namespace Lyra.UI.Components;

public static class ComponentExtensions
{
    // --------------------------------------------------------
    //  Sizing — value + matching SizeMode
    // --------------------------------------------------------

    public static T Width<T>(this T c, float value) where T : ComponentBase
    {
        c.HorizontalSize = SizeMode.Fixed;
        c.Width = value;
        return c;
    }

    public static T Height<T>(this T c, float value) where T : ComponentBase
    {
        c.VerticalSize = SizeMode.Fixed;
        c.Height = value;
        return c;
    }

    public static T Size<T>(this T c, float width, float height) where T : ComponentBase
        => c.Width(width).Height(height);

    // --------------------------------------------------------
    //  Size modes
    // --------------------------------------------------------

    public static T Expand<T>(this T c) where T : ComponentBase
    {
        c.HorizontalSize = SizeMode.Expand;
        c.VerticalSize = SizeMode.Expand;
        return c;
    }

    public static T ExpandH<T>(this T c) where T : ComponentBase
    {
        c.HorizontalSize = SizeMode.Expand;
        return c;
    }

    public static T ExpandV<T>(this T c) where T : ComponentBase
    {
        c.VerticalSize = SizeMode.Expand;
        return c;
    }

    public static T FlexibleH<T>(this T c) where T : ComponentBase
    {
        c.HorizontalSize = SizeMode.Flexible;
        return c;
    }

    public static T FlexibleV<T>(this T c) where T : ComponentBase
    {
        c.VerticalSize = SizeMode.Flexible;
        return c;
    }

    public static T ShrinkH<T>(this T c) where T : ComponentBase
    {
        c.HorizontalSize = SizeMode.Shrink;
        return c;
    }

    public static T ShrinkV<T>(this T c) where T : ComponentBase
    {
        c.VerticalSize = SizeMode.Shrink;
        return c;
    }

    // --------------------------------------------------------
    //  Constraints — applied regardless of SizeMode
    // --------------------------------------------------------

    public static T MinWidth<T>(this T c, float value) where T : ComponentBase
    {
        c.MinWidth = value;
        return c;
    }

    public static T MaxWidth<T>(this T c, float value) where T : ComponentBase
    {
        c.MaxWidth = value;
        return c;
    }

    public static T MinHeight<T>(this T c, float value) where T : ComponentBase
    {
        c.MinHeight = value;
        return c;
    }

    public static T MaxHeight<T>(this T c, float value) where T : ComponentBase
    {
        c.MaxHeight = value;
        return c;
    }

    // --------------------------------------------------------
    //  Padding
    // --------------------------------------------------------

    public static T Padding<T>(this T c, float all) where T : ComponentBase
    {
        c.Padding = new Padding(all);
        return c;
    }

    public static T Padding<T>(this T c, float horizontal, float vertical) where T : ComponentBase
    {
        c.Padding = new Padding(horizontal, vertical, horizontal, vertical);
        return c;
    }

    public static T Padding<T>(this T c, float left, float top, float right, float bottom) where T : ComponentBase
    {
        c.Padding = new Padding(left, top, right, bottom);
        return c;
    }

    public static T PadTop<T>(this T c, float value) where T : ComponentBase
    {
        c.Padding = c.Padding with { Top = value };
        return c;
    }

    public static T PadBottom<T>(this T c, float value) where T : ComponentBase
    {
        c.Padding = c.Padding with { Bottom = value };
        return c;
    }

    public static T PadLeft<T>(this T c, float value) where T : ComponentBase
    {
        c.Padding = c.Padding with { Left = value };
        return c;
    }

    public static T PadRight<T>(this T c, float value) where T : ComponentBase
    {
        c.Padding = c.Padding with { Right = value };
        return c;
    }

    // --------------------------------------------------------
    //  Alignment
    // --------------------------------------------------------

    public static T Align<T>(this T c, HAlign horizontal) where T : ComponentBase
    {
        c.HorizontalAlign = horizontal;
        return c;
    }

    public static T Align<T>(this T c, VAlign vertical) where T : ComponentBase
    {
        c.VerticalAlign = vertical;
        return c;
    }

    public static T Align<T>(this T c, HAlign horizontal, VAlign vertical) where T : ComponentBase
    {
        c.HorizontalAlign = horizontal;
        c.VerticalAlign = vertical;
        return c;
    }

    public static T Center<T>(this T c) where T : ComponentBase
        => c.Align(HAlign.Center, VAlign.Center);

    public static T CenterV<T>(this T c) where T : ComponentBase
        => c.Align(VAlign.Center);

    // --------------------------------------------------------
    //  Presence
    // --------------------------------------------------------

    public static T Present<T>(this T c, bool value = true) where T : ComponentBase
    {
        c.Present = value;
        return c;
    }

    public static T Visible<T>(this T c, bool value = true) where T : ComponentBase
    {
        c.Visible = value;
        return c;
    }

    public static T Enabled<T>(this T c, bool value = true) where T : ComponentBase
    {
        c.Enabled = value;
        return c;
    }

    // --------------------------------------------------------
    //  Visuals & hit-testing
    // --------------------------------------------------------

    public static T Background<T>(this T c, SKColor color) where T : ComponentBase
    {
        c.BackgroundColor = color;
        return c;
    }

    /// <summary>Excludes the component from hit-testing, so pointer events pass through.</summary>
    public static T Transient<T>(this T c, bool value = true) where T : ComponentBase
    {
        c.Transient = value;
        return c;
    }

    public static T Resizable<T>(this T c, ResizeEdge edges) where T : ComponentBase
    {
        c.ResizeEdges = edges;
        return c;
    }

    // --------------------------------------------------------
    //  Escape hatch
    // --------------------------------------------------------

    /// <summary>
    /// Applies arbitrary configuration without breaking the chain. For the
    /// cases these extensions do not cover, and for conditional setup:
    /// .Apply(c => { if (cond) c.Padding = ...; })
    /// </summary>
    public static T Apply<T>(this T c, Action<T> configure) where T : ComponentBase
    {
        configure(c);
        return c;
    }
}
