using Lyra.UI.SupportingTypes;
using Lyra.UI.Theme;
using SkiaSharp;

namespace Lyra.UI.Components.Controls;

/// <summary>
/// Bordered panel that stacks <see cref="MenuItem"/> rows and stretches
/// each to the widest, so check marks, labels and shortcuts align.
/// Shared by <see cref="ContextMenu"/> and <see cref="DropDownMenu{T}"/>.
/// </summary>
internal sealed class MenuPanel : ComponentBase, IContainer
{
    public const float PanelPadding = 4f;

    private readonly List<IComponent> _children = [];

    public IReadOnlyList<IComponent> Children => _children;

    public MenuPanel()
    {
        Padding = new Padding(PanelPadding);
        BackgroundColor = Palette.Panel;
    }

    private bool _hasSubmenuRow;

    public void AddComponent(IComponent child)
    {
        child.Parent = this;
        _children.Add(child);

        if (child is not MenuItem item)
            return;

        if (item.HasSubmenu && !_hasSubmenuRow)
        {
            _hasSubmenuRow = true;

            foreach (var row in _children)
            {
                if (row is MenuItem existing)
                    existing.ReserveArrowSlot = true;
            }
        }

        item.ReserveArrowSlot = _hasSubmenuRow;
    }

    public void AddComponents(params IComponent[] children)
    {
        foreach (var child in children)
            AddComponent(child);
    }

    protected override SKSize MeasureContent(SKSize availableSize)
    {
        var width = 0f;
        var height = 0f;

        foreach (var child in _children)
        {
            if (!child.Present)
                continue;

            var size = child.Measure(availableSize);
            width = Math.Max(width, size.Width);
            height += size.Height;
        }

        return new SKSize(width, height);
    }

    protected override void ResolveContent()
    {
        foreach (var child in _children)
            child.Resolve();
    }

    protected override void ArrangeContent(SKRect contentBounds)
    {
        var y = contentBounds.Top;

        foreach (var child in _children)
        {
            if (!child.Present)
                continue;

            var h = child.DesiredSize.Height;
            child.Arrange(new SKRect(contentBounds.Left, y, contentBounds.Right, y + h));
            y += h;
        }
    }

    protected override void RenderContent(SKCanvas canvas, SKRect contentBounds)
    {
        using var border = new SKPaint();
        border.Color = Palette.Border;
        border.IsStroke = true;
        border.StrokeWidth = 1f;
        border.IsAntialias = true;

        canvas.DrawRect(SKRect.Inflate(ArrangedBounds, -0.5f, -0.5f), border);

        foreach (var child in _children)
        {
            if (child.Present)
                child.Render(canvas);
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            foreach (var child in _children)
                child.Dispose();
        }

        base.Dispose(disposing);
    }
}

/// <summary>A thin rule between groups of menu rows.</summary>
internal sealed class MenuSeparator : ComponentBase
{
    private const float LineInset = 4f;

    public MenuSeparator()
    {
        Height = 9f;
        VerticalSize = SizeMode.Fixed;
        Transient = true;
    }

    protected override SKSize MeasureContent(SKSize availableSize) => new(0f, Height ?? 0f);

    protected override void ArrangeContent(SKRect contentBounds) { }

    protected override void RenderContent(SKCanvas canvas, SKRect contentBounds)
    {
        using var paint = new SKPaint();
        paint.Color = Palette.Border;
        paint.StrokeWidth = 1f;

        var y = MathF.Floor(contentBounds.MidY) + 0.5f;
        canvas.DrawLine(contentBounds.Left + LineInset, y, contentBounds.Right - LineInset, y, paint);
    }
}
