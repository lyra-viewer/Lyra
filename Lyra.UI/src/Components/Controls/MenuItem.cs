using Lyra.UI.Components.Controls.Button;
using Lyra.UI.Components.Primitives;
using Lyra.UI.SupportingTypes;
using SkiaSharp;

namespace Lyra.UI.Components.Controls;

/// <summary>
/// A selectable row used inside menus and pickers.
///
/// Layout is label-on-left with a check indicator pinned to the
/// right edge, independent of label width. The check is only
/// rendered when <see cref="IsPicked"/> is true, but its space is
/// always reserved so labels align vertically across items.
///
/// Hover and press visuals are delegated to <see cref="ButtonDrawer"/>
/// with <see cref="ButtonVariant.Ghost"/> for consistency with the
/// rest of the UI — transparent default, subtle hover background.
/// </summary>
public class MenuItem : ComponentBase, IPickable
{
    private readonly Label _label;
    private readonly SvgImage _checkIcon;

    private bool _isPressed;
    private bool _isHovered;

    // External events
    public event Action? Click;

    // Pickable state
    public bool IsPicked { get; set; }

    // Visuals
    public float CornerRadius { get; set; }

    public string Text
    {
        get => _label.Text;
        set => _label.Text = value;
    }

    // --------------------------------------------------------
    //  Constructor
    // --------------------------------------------------------

    public MenuItem(string text, float iconSize = 16f)
    {
        MinHeight = 30;

        _label = new Label(text)
        {
            Transient = true,
            HorizontalAlign = HAlign.Left,
            VerticalAlign = VAlign.Center,
            Padding = new Padding(2)
        };

        _checkIcon = new SvgImage(ResourceLoader.GetSvg("check_small"), iconSize, iconSize);
    }

    // --------------------------------------------------------
    //  Layout
    // --------------------------------------------------------

    protected override SKSize MeasureContent(SKSize availableSize)
    {
        var labelSize = _label.Measure(availableSize);
        var iconSize = _checkIcon.Measure(availableSize);

        // Always reserve space for the check, even when not picked,
        // so sibling items align vertically on both the label and the indicator.
        var width = labelSize.Width + ButtonDrawer.IconTextGap + iconSize.Width;
        var height = Math.Max(labelSize.Height, iconSize.Height);

        return new SKSize(
            width + ButtonDrawer.ContentPadH * 2,
            height + ButtonDrawer.ContentPadV * 2);
    }

    protected override void ArrangeContent(SKRect contentBounds)
    {
        var inner = new SKRect(
            contentBounds.Left + ButtonDrawer.ContentPadH,
            contentBounds.Top + ButtonDrawer.ContentPadV,
            contentBounds.Right - ButtonDrawer.ContentPadH,
            contentBounds.Bottom - ButtonDrawer.ContentPadV);

        var labelW = _label.DesiredSize.Width;
        var iconW = _checkIcon.DesiredSize.Width;

        // Label anchored to the left edge
        _label.Arrange(new SKRect(
            inner.Left,
            inner.Top,
            inner.Left + labelW,
            inner.Bottom));

        // Check pinned to the right edge (space is reserved regardless of IsPicked)
        _checkIcon.Arrange(new SKRect(
            inner.Right - iconW,
            inner.Top,
            inner.Right,
            inner.Bottom));
    }

    // --------------------------------------------------------
    //  Render
    // --------------------------------------------------------

    protected override void RenderContent(SKCanvas canvas, SKRect contentBounds)
    {
        ButtonDrawer.DrawBackground(canvas, contentBounds, ButtonVariant.Ghost, CornerRadius, _isHovered, _isPressed);

        _label.Color = ButtonDrawer.GetTextColor(ButtonVariant.Ghost);
        _label.Render(canvas);

        if (IsPicked)
            _checkIcon.Render(canvas);
    }

    // --------------------------------------------------------
    //  Input
    // --------------------------------------------------------

    protected virtual void OnClick()
    {
        Click?.Invoke();
    }

    protected override void OnPointerDownCore(SKPoint point)
    {
        if (!IsEffectivelyEnabled)
            return;

        _isPressed = true;
    }

    protected override void OnPointerUpCore(SKPoint point)
    {
        if (!IsEffectivelyEnabled)
            return;

        if (!_isPressed)
            return;

        _isPressed = false;
        OnClick();
    }

    protected override void OnPointerEnterCore()
    {
        if (!IsEffectivelyEnabled)
            return;

        _isHovered = true;
    }

    protected override void OnPointerLeaveCore()
    {
        _isHovered = false;
        _isPressed = false;
    }

    // --------------------------------------------------------
    //  Dispose
    // --------------------------------------------------------

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _label.Dispose();
            _checkIcon.Dispose();
        }

        base.Dispose(disposing);
    }
}