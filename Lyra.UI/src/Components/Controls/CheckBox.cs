using Lyra.UI.Components.Controls.Button;
using Lyra.UI.Components.Primitives;
using Lyra.UI.SupportingTypes;
using SkiaSharp;

namespace Lyra.UI.Components.Controls;

/// <summary>
/// A labelled check box: an SVG box icon (checked / blank) followed by a caption.
/// Behaves like a single click target with the same hover / pressed background
/// feedback as <see cref="Button.Button"/> (Ghost variant).
/// </summary>
public class CheckBox : ComponentBase
{
    private const float ContentPadH = 6f;
    private const float ContentPadV = 4f;

    private readonly Label _label;
    private readonly SvgImage _checkedIcon;
    private readonly SvgImage _blankIcon;

    /// <summary>Raised when the checked state changes through user interaction or <see cref="Toggle"/>.</summary>
    public event Action<bool>? CheckedChanged;

    private bool _checked;
    private bool _isHovered;
    private bool _isPressed;

    public float IconTextGap { get; set; } = 6f;
    public float CornerRadius { get; set; } = 4f;

    /// <summary>Current state. Setting this does not raise <see cref="CheckedChanged"/>.</summary>
    public bool Checked
    {
        get => _checked;
        set => _checked = value;
    }

    public string Text
    {
        get => _label.Text;
        set => _label.Text = value;
    }

    private SvgImage CurrentIcon => _checked ? _checkedIcon : _blankIcon;

    public CheckBox(string text, float iconSize = 20f)
    {
        MinHeight = 28f;

        _checkedIcon = new SvgImage(ResourceLoader.GetSvg("check_box"), iconSize, iconSize);
        _blankIcon = new SvgImage(ResourceLoader.GetSvg("check_box_outline_blank"), iconSize, iconSize);

        _label = new Label(text)
        {
            Transient = true,
            VerticalAlign = VAlign.Center,
            Padding = new Padding(2)
        };
    }

    protected override SKSize MeasureContent(SKSize availableSize)
    {
        var iconSize = _blankIcon.Measure(availableSize);
        _checkedIcon.Measure(availableSize);
        var labelSize = _label.Measure(availableSize);

        var width = iconSize.Width + IconTextGap + labelSize.Width;
        var height = Math.Max(iconSize.Height, labelSize.Height);

        return new SKSize(width + ContentPadH * 2, height + ContentPadV * 2);
    }

    protected override void ArrangeContent(SKRect contentBounds)
    {
        var inner = new SKRect(
            contentBounds.Left + ContentPadH,
            contentBounds.Top + ContentPadV,
            contentBounds.Right - ContentPadH,
            contentBounds.Bottom - ContentPadV);

        var iconW = _blankIcon.DesiredSize.Width;
        var iconH = _blankIcon.DesiredSize.Height;
        var iconTop = inner.MidY - iconH / 2f;
        var iconRect = new SKRect(inner.Left, iconTop, inner.Left + iconW, iconTop + iconH);

        _checkedIcon.Arrange(iconRect);
        _blankIcon.Arrange(iconRect);

        var labelLeft = inner.Left + iconW + IconTextGap;
        _label.Arrange(new SKRect(labelLeft, inner.Top, inner.Right, inner.Bottom));
    }

    protected override void RenderContent(SKCanvas canvas, SKRect contentBounds)
    {
        ButtonDrawer.DrawBackground(canvas, contentBounds, ButtonVariant.Ghost, CornerRadius, _isHovered, _isPressed);
        CurrentIcon.Render(canvas);
        _label.Render(canvas);
    }

    public void Toggle()
    {
        _checked = !_checked;
        CheckedChanged?.Invoke(_checked);
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

        var wasPressed = _isPressed;
        _isPressed = false;

        if (wasPressed && _isHovered)
            Toggle();
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
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _label.Dispose();
            _checkedIcon.Dispose();
            _blankIcon.Dispose();
        }

        base.Dispose(disposing);
    }
}