using Lyra.UI.Components.Controls.Button;
using Lyra.UI.Components.Primitives;
using Lyra.UI.SupportingTypes;
using Lyra.UI.Theme;
using SkiaSharp;

namespace Lyra.UI.Components.Controls;

/// <summary>
/// A row inside a <see cref="ContextMenu"/> or <see cref="DropDownMenu{T}"/>.
///
/// Layout: [check] label ··· shortcut [arrow]. The check slot is
/// always reserved, the arrow slot only in panels holding a submenu,
/// and the panel stretches every row to its widest, so labels and
/// right-aligned shortcuts line up.
/// </summary>
public class MenuItem : ComponentBase
{
    private const float ColumnGap = 16f;

    private readonly Label _label;
    private readonly Label? _shortcut;
    private readonly SvgImage _checkIcon;
    private readonly SvgImage? _arrowIcon;

    private bool _isPressed;
    private bool _isHovered;

    public event Action? Click;

    public event Action? Hovered;

    private bool _isChecked;

    public bool IsChecked
    {
        get => _isChecked;
        set => Set(ref _isChecked, value);
    }

    // Keeps a submenu header lit while its submenu is open.
    private bool _isHighlighted;

    internal bool IsHighlighted
    {
        get => _isHighlighted;
        set => Set(ref _isHighlighted, value);
    }

    internal bool HasSubmenu => _arrowIcon is not null;

    // Set by the panel so shortcuts align with those of submenu rows.
    private bool _reserveArrowSlot;

    internal bool ReserveArrowSlot
    {
        get => _reserveArrowSlot;
        set => Set(ref _reserveArrowSlot, value);
    }

    public string Text
    {
        get => _label.Text;
        set => _label.Text = value;
    }

    // --------------------------------------------------------
    //  Constructor
    // --------------------------------------------------------

    public MenuItem(string text, string? shortcut = null, bool hasSubmenu = false, float iconSize = 16f)
    {
        MinHeight = 24;

        _label = new Label(text)
        {
            HorizontalAlign = HAlign.Left,
            VerticalAlign = VAlign.Center,
            Padding = new Padding(2)
        };

        if (!string.IsNullOrEmpty(shortcut))
        {
            _shortcut = new Label(shortcut)
            {
                HorizontalAlign = HAlign.Right,
                VerticalAlign = VAlign.Center,
                Padding = new Padding(2),
                Color = Palette.Dim
            };
        }

        _checkIcon = new SvgImage(ResourceLoader.GetSvg("check_small"), iconSize, iconSize);

        if (hasSubmenu)
            _arrowIcon = new SvgImage(ResourceLoader.GetSvg("arrow_drop_right"), iconSize, iconSize);

        _reserveArrowSlot = hasSubmenu;
    }

    // --------------------------------------------------------
    //  Layout
    // --------------------------------------------------------

    protected override SKSize MeasureContent(SKSize availableSize)
    {
        var labelSize = _label.Measure(availableSize);
        var shortcutSize = _shortcut?.Measure(availableSize) ?? SKSize.Empty;
        var iconSize = _checkIcon.Measure(availableSize);
        _arrowIcon?.Measure(availableSize);

        var trailing = _shortcut is not null ? ColumnGap + shortcutSize.Width : 0f;
        var arrowSlot = ReserveArrowSlot ? ButtonDrawer.IconTextGap + iconSize.Width : 0f;

        var width = iconSize.Width + ButtonDrawer.IconTextGap + labelSize.Width + trailing + arrowSlot;
        var height = Math.Max(Math.Max(labelSize.Height, shortcutSize.Height), iconSize.Height);

        return new SKSize(width + ButtonDrawer.ContentPadH * 2, height + ButtonDrawer.ContentPadV * 2);
    }

    protected override void PropagateContext(UIContext? context)
    {
        _label.Context = context;
        _checkIcon.Context = context;

        if (_shortcut is not null)
            _shortcut.Context = context;

        if (_arrowIcon is not null)
            _arrowIcon.Context = context;
    }

    protected override void ArrangeContent(SKRect contentBounds)
    {
        var inner = new SKRect(
            contentBounds.Left + ButtonDrawer.ContentPadH,
            contentBounds.Top + ButtonDrawer.ContentPadV,
            contentBounds.Right - ButtonDrawer.ContentPadH,
            contentBounds.Bottom - ButtonDrawer.ContentPadV);

        var iconW = _checkIcon.DesiredSize.Width;

        _checkIcon.Arrange(new SKRect(inner.Left, inner.Top, inner.Left + iconW, inner.Bottom));

        var labelLeft = inner.Left + iconW + ButtonDrawer.IconTextGap;
        _label.Arrange(new SKRect(labelLeft, inner.Top, labelLeft + _label.DesiredSize.Width, inner.Bottom));

        var arrowLeft = inner.Right - iconW;
        _arrowIcon?.Arrange(new SKRect(arrowLeft, inner.Top, inner.Right, inner.Bottom));

        if (_shortcut is not null)
        {
            var shortcutRight = ReserveArrowSlot ? arrowLeft - ButtonDrawer.IconTextGap : inner.Right;
            _shortcut.Arrange(new SKRect(shortcutRight - _shortcut.DesiredSize.Width, inner.Top, shortcutRight, inner.Bottom));
        }
    }

    // --------------------------------------------------------
    //  Render
    // --------------------------------------------------------

    protected override void RenderContent(SKCanvas canvas, SKRect contentBounds)
    {
        ButtonDrawer.DrawBackground(canvas, contentBounds, ButtonVariant.Ghost, 0f, _isHovered || IsHighlighted, _isPressed);

        _label.Color = ButtonDrawer.GetTextColor(ButtonVariant.Ghost);
        _label.Render(canvas);

        _shortcut?.Render(canvas);
        _arrowIcon?.Render(canvas);

        if (IsChecked)
            _checkIcon.Render(canvas);
    }

    // --------------------------------------------------------
    //  Input
    // --------------------------------------------------------

    internal void ResetInteraction()
    {
        _isHovered = false;
        _isPressed = false;
        IsHighlighted = false;
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

        var wasPressed = _isPressed;
        _isPressed = false;

        if (wasPressed && _isHovered)
            Click?.Invoke();
    }

    protected override void OnPointerEnterCore()
    {
        if (!IsEffectivelyEnabled)
            return;

        _isHovered = true;
        Hovered?.Invoke();
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
            _shortcut?.Dispose();
            _checkIcon.Dispose();
            _arrowIcon?.Dispose();
        }

        base.Dispose(disposing);
    }
}