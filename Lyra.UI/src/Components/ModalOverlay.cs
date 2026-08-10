using Lyra.UI.SupportingTypes;
using SkiaSharp;

namespace Lyra.UI.Components;

/// <summary>
/// Fills the canvas with a dimming scrim and centers a single child.
/// Used as the root of the modal layer.
///
/// Like <see cref="Overlay"/>, the child is an external reference -
/// ModalOverlay does not dispose it. A pointer-down that lands on the
/// scrim (i.e. is not absorbed by the centered content) fires
/// <see cref="_outsideClicked"/>, which the modal host uses to dismiss.
///
/// For outside-click dismissal to work the content root must be
/// hit-testable (non-transient) so clicks inside it resolve to the
/// content rather than falling through to this overlay.
/// </summary>
public sealed class ModalOverlay : ComponentBase, IContainer
{
    private static readonly SKColor ScrimColor = new(0, 0, 0, 160);

    private readonly IComponent _content;
    private readonly Action _outsideClicked;
    private readonly IComponent[] _children;

    public IReadOnlyList<IComponent> Children => _children;

    public ModalOverlay(IComponent content, Action outsideClicked)
    {
        _content = content;
        _outsideClicked = outsideClicked;
        _children = [content];

        HorizontalSize = SizeMode.Expand;
        VerticalSize = SizeMode.Expand;

        content.Parent = this;
    }

    protected override SKSize MeasureContent(SKSize availableSize)
    {
        _content.Measure(availableSize);
        return availableSize;
    }

    protected override void ResolveContent()
    {
        _content.Resolve();
    }

    protected override void ArrangeContent(SKRect contentBounds)
    {
        var size = _content.DesiredSize;
        var left = contentBounds.MidX - size.Width / 2f;
        var top = contentBounds.MidY - size.Height / 2f;
        _content.Arrange(new SKRect(left, top, left + size.Width, top + size.Height));
    }

    protected override void RenderContent(SKCanvas canvas, SKRect contentBounds)
    {
        using (var paint = new SKPaint())
        {
            paint.Color = ScrimColor;
            canvas.DrawRect(ArrangedBounds, paint);
        }

        _content.Render(canvas);
    }

    protected override void OnPointerDownCore(SKPoint point)
    {
        // Hit-testing routes clicks on the child to deeper components,
        // so reaching here means the click landed on the scrim.
        _outsideClicked();
    }

    // --------------------------------------------------------
    //  IContainer - content is fixed at construction
    // --------------------------------------------------------

    public void AddComponent(IComponent child) =>
        throw new NotSupportedException("ModalOverlay content is defined via its constructor.");

    public void AddComponents(params IComponent[] children) =>
        throw new NotSupportedException("ModalOverlay content is defined via its constructor.");
}
