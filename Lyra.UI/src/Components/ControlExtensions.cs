using Lyra.UI.Components.Controls;
using Lyra.UI.Components.Controls.Button;
using Lyra.UI.Components.Layout;
using Lyra.UI.Components.Primitives;
using Lyra.UI.SupportingTypes;
using SkiaSharp;

namespace Lyra.UI.Components;

public static class ControlExtensions
{
    // --------------------------------------------------------
    //  Label
    // --------------------------------------------------------

    public static Label Text(this Label c, string value)
    {
        c.Text = value;
        return c;
    }

    public static Label Color(this Label c, SKColor value)
    {
        c.Color = value;
        return c;
    }

    public static Label FontSize(this Label c, float value)
    {
        c.FontSize = value;
        return c;
    }

    public static Label FontFamily(this Label c, string value)
    {
        c.FontFamily = value;
        return c;
    }

    public static Label Bold(this Label c, bool value = true)
    {
        c.Bold = value;
        return c;
    }

    public static Label Italic(this Label c, bool value = true)
    {
        c.Italic = value;
        return c;
    }

    public static Label Underline(this Label c, bool value = true)
    {
        c.Underline = value;
        return c;
    }
    
    public static Label Ellipsize(this Label c, bool value = true)
    {
        c.Ellipsize = value;
        return c;
    }

    public static Label Antialias(this Label c, bool value = true)
    {
        c.Antialias = value;
        return c;
    }

    // --------------------------------------------------------
    //  Stacks
    // --------------------------------------------------------

    public static T Spacing<T>(this T c, float value) where T : StackBase
    {
        c.Spacing = value;
        return c;
    }

    public static T ContentAlign<T>(this T c, HAlign value) where T : StackBase
    {
        c.ContentAlign = value;
        return c;
    }

    public static VScrollContainer Spacing(this VScrollContainer c, float value)
    {
        c.Spacing = value;
        return c;
    }

    public static VScrollContainer ScrollSpeed(this VScrollContainer c, float value)
    {
        c.ScrollSpeed = value;
        return c;
    }

    // --------------------------------------------------------
    //  Collapsible
    // --------------------------------------------------------

    public static Collapsible Title(this Collapsible c, string value)
    {
        c.Title = value;
        return c;
    }

    public static Collapsible Expanded(this Collapsible c, bool value = true)
    {
        c.IsExpanded = value;
        return c;
    }

    public static Collapsible OnToggled(this Collapsible c, Action handler)
    {
        c.Toggled += handler;
        return c;
    }

    // --------------------------------------------------------
    //  Button
    // --------------------------------------------------------

    public static Button Variant(this Controls.Button.Button c, ButtonVariant value)
    {
        c.Variant = value;
        return c;
    }

    public static Button CornerRadius(this Controls.Button.Button c, float value)
    {
        c.CornerRadius = value;
        return c;
    }

    public static Button ContentAlign(this Controls.Button.Button c, HAlign value)
    {
        c.ContentAlign = value;
        return c;
    }

    public static Button Icon(this Controls.Button.Button c, ButtonIcon position, ImageBase image)
    {
        c.Icon = position;
        c.IconImage = image;
        return c;
    }

    public static Button Content(this Controls.Button.Button c, IComponent value)
    {
        c.Content = value;
        return c;
    }

    // --------------------------------------------------------
    //  CheckBox
    // --------------------------------------------------------

    public static CheckBox Checked(this CheckBox c, bool value = true)
    {
        c.Checked = value;
        return c;
    }

    public static CheckBox OnCheckedChanged(this CheckBox c, Action<bool> handler)
    {
        c.CheckedChanged += handler;
        return c;
    }

    // --------------------------------------------------------
    //  DropDownMenu
    // --------------------------------------------------------

    public static DropDownMenu<T> Selected<T>(this DropDownMenu<T> c, T value) where T : notnull
    {
        c.Selected = value;
        return c;
    }

    public static DropDownMenu<T> OnSelectionChanged<T>(this DropDownMenu<T> c, Action<T> handler) where T : notnull
    {
        c.SelectionChanged += handler;
        return c;
    }

    // --------------------------------------------------------
    //  ValueSlider
    // --------------------------------------------------------

    public static ValueSlider OnValueChanged(this ValueSlider c, Action<int> handler)
    {
        c.ValueChanged += handler;
        return c;
    }

    // --------------------------------------------------------
    //  Images
    // --------------------------------------------------------

    public static T ImageSize<T>(this T c, float width, float height) where T : ImageBase
    {
        c.ImageWidth = width;
        c.ImageHeight = height;
        return c;
    }

    public static Image Source(this Image c, SKImage? value)
    {
        c.Source = value;
        return c;
    }
}
