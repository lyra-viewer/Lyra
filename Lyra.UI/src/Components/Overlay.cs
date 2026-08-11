using Lyra.UI.SupportingTypes;
using SkiaSharp;

namespace Lyra.UI.Components;

/// <summary>
/// Fills the canvas and positions a single child at an absolute
/// canvas-space point. Used as the root of popup layers.
///
/// The child is treated as an external reference — Overlay does
/// not dispose it. A click that lands inside the overlay but not
/// on the child fires <see cref="OutsideClicked"/>, which the
/// popup host uses to dismiss.
/// </summary>
public sealed class Overlay : ComponentBase, IContainer
{
    private readonly IComponent _content;
    private readonly SKPoint _position;
    private readonly Action _outsideClicked;
    private readonly IComponent[] _children;

    public IReadOnlyList<IComponent> Children => _children;

    public Overlay(IComponent content, SKPoint position, Action outsideClicked)
    {
        _content = content;
        _position = position;
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
        _content.Arrange(new SKRect(
            _position.X,
            _position.Y,
            _position.X + size.Width,
            _position.Y + size.Height));
    }

    protected override void RenderContent(SKCanvas canvas, SKRect contentBounds)
    {
        _content.Render(canvas);
    }

    protected override void OnPointerDownCore(SKPoint point)
    {
        // Hit-testing routes clicks on the child to deeper components,
        // so reaching here means the click was outside the child.
        _outsideClicked();
    }

    // --------------------------------------------------------
    //  IContainer - content is fixed at construction
    // --------------------------------------------------------

    public void AddComponent(IComponent child) =>
        throw new NotSupportedException("Overlay content is defined via its constructor.");

    public void AddComponents(params IComponent[] children) =>
        throw new NotSupportedException("Overlay content is defined via its constructor.");

    // --------------------------------------------------------
    //  Dispose - detach, never dispose the content
    // --------------------------------------------------------
    //  The content outlives this overlay (a dropdown reuses its options panel
    //  across opens), so leaving Parent pointing here would keep a discarded
    //  overlay reachable and make the panel's effective visibility depend on
    //  an object no longer in any layer.
    // --------------------------------------------------------

    protected override void Dispose(bool disposing)
    {
        if (disposing && ReferenceEquals(_content.Parent, this))
            _content.Parent = null;

        base.Dispose(disposing);
    }
}