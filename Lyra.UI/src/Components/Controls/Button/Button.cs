using Lyra.UI.Components.Primitives;
using Lyra.UI.SupportingTypes;
using SkiaSharp;

namespace Lyra.UI.Components.Controls.Button;

public class Button : ComponentBase
{
    private readonly Label _label;

    // External events
    public event Action? Click;

    // Visual state
    private bool _isPressed;
    private bool _isHovered;

    // Variant
    public ButtonVariant Variant { get; set; }
    public ButtonIcon Icon { get; set; } = ButtonIcon.None;
    public float CornerRadius { get; set; } = 4f;

    // Content alignment within the button
    public HAlign ContentAlign { get; set; } = HAlign.Center;

    // Icon - externally provided, caller owns lifetime
    public ImageBase? IconImage { get; set; }

    public string Text
    {
        get => _label.Text;
        set => _label.Text = value;
    }

    private bool HasIcon => Icon != ButtonIcon.None && IconImage != null;
    private bool IsIconOnly => Icon == ButtonIcon.Only && IconImage != null;

    // --------------------------------------------------------
    //  Constructors
    // --------------------------------------------------------

    public Button() : this(string.Empty) { }

    public Button(string text, ButtonVariant variant = ButtonVariant.Default)
    {
        Variant = variant;
        MinWidth = 64;
        MinHeight = 30;

        _label = new Label(text)
        {
            Transient = true,
            HorizontalAlign = HAlign.Center,
            VerticalAlign = VAlign.Center,
            Padding = new Padding(2)
        };
    }

    // --------------------------------------------------------
    //  Layout
    // --------------------------------------------------------

    protected override SKSize MeasureContent(SKSize availableSize)
    {
        var labelSize = IsIconOnly ? SKSize.Empty : _label.Measure(availableSize);
        var iconSize = HasIcon ? IconImage!.Measure(availableSize) : SKSize.Empty;

        var width = ComputeContentWidth(labelSize.Width, iconSize.Width);
        var height = Math.Max(labelSize.Height, iconSize.Height);

        return new SKSize(width + ButtonDrawer.ContentPadH * 2, height + ButtonDrawer.ContentPadV * 2);
    }

    protected override void ArrangeContent(SKRect contentBounds)
    {
        // Inset by content padding
        var inner = new SKRect(
            contentBounds.Left + ButtonDrawer.ContentPadH,
            contentBounds.Top + ButtonDrawer.ContentPadV,
            contentBounds.Right - ButtonDrawer.ContentPadH,
            contentBounds.Bottom - ButtonDrawer.ContentPadV);

        var labelW = IsIconOnly ? 0f : _label.DesiredSize.Width;
        var iconW = HasIcon ? IconImage!.DesiredSize.Width : 0f;
        var totalWidth = ComputeContentWidth(labelW, iconW);

        var startX = ContentAlign switch
        {
            HAlign.Left => inner.Left,
            HAlign.Right => inner.Right - totalWidth,
            _ => inner.MidX - totalWidth / 2f
        };

        // Arrange in a single left-to-right pass based on Icon position.
        var cursor = startX;

        if (HasIcon && (Icon == ButtonIcon.Left || IsIconOnly))
        {
            IconImage!.Arrange(new SKRect(cursor, inner.Top, cursor + iconW, inner.Bottom));
            cursor += iconW;
            if (!IsIconOnly)
                cursor += ButtonDrawer.IconTextGap;
        }

        if (!IsIconOnly)
        {
            _label.Arrange(new SKRect(cursor, inner.Top, cursor + labelW, inner.Bottom));
            cursor += labelW;
        }

        if (HasIcon && Icon == ButtonIcon.Right)
        {
            cursor += ButtonDrawer.IconTextGap;
            IconImage!.Arrange(new SKRect(cursor, inner.Top, cursor + iconW, inner.Bottom));
        }
    }

    private float ComputeContentWidth(float labelW, float iconW)
    {
        if (IsIconOnly)
            return iconW;

        if (HasIcon)
            return labelW + iconW + ButtonDrawer.IconTextGap;

        return labelW;
    }

    // --------------------------------------------------------
    //  Render
    // --------------------------------------------------------

    protected override void RenderContent(SKCanvas canvas, SKRect contentBounds)
    {
        ButtonDrawer.DrawBackground(canvas, contentBounds, Variant, CornerRadius, _isHovered, _isPressed);

        _label.Color = ButtonDrawer.GetTextColor(Variant);
        _label.Underline = Variant == ButtonVariant.Link && (_isHovered || _isPressed);

        if (HasIcon)
            IconImage!.Render(canvas);

        if (!IsIconOnly)
            _label.Render(canvas);
    }

    // --------------------------------------------------------
    //  Input
    // --------------------------------------------------------

    protected virtual void OnClick()
    {
        Click?.Invoke();
    }

    public override void OnPointerDown(SKPoint point)
    {
        if (!IsEffectivelyEnabled)
            return;

        _isPressed = true;
    }

    public override void OnPointerUp(SKPoint point)
    {
        if (!IsEffectivelyEnabled)
            return;

        if (!_isPressed)
            return;

        _isPressed = false;
        OnClick();
    }

    public override void OnPointerEnter()
    {
        if (!IsEffectivelyEnabled)
            return;

        _isHovered = true;
    }

    public override void OnPointerLeave()
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
            // IconImage is externally provided - caller owns its lifetime.
        }

        base.Dispose(disposing);
    }
}