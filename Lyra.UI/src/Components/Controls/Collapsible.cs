using Lyra.UI.Components.Controls.Button;
using Lyra.UI.Components.Layout;
using Lyra.UI.Components.Primitives;
using Lyra.UI.SupportingTypes;
using SkiaSharp;

namespace Lyra.UI.Components.Controls;

public class Collapsible : ComponentBase, IContainer
{
    private readonly VStack _mainContainer;
    private readonly Button.Button _headerButton;
    private readonly VStack _content;

    private readonly SvgImage _collapsedIcon;
    private readonly SvgImage _expandedIcon;

    private bool _isExpanded;

    public bool IsExpanded
    {
        get => _isExpanded;
        set
        {
            _isExpanded = value;
            _content.Present = value;
            _headerButton.IconImage = value ? _expandedIcon : _collapsedIcon;
        }
    }

    public string Title
    {
        get => _headerButton.Text;
        set => _headerButton.Text = value;
    }

    /// Fired after expand/collapse toggle.
    public event Action? Toggled;

    // Exposes internal structure for hit-testing.
    // AddComponent routes to _content, so external additions
    // end up in the right place.
    public IReadOnlyList<IComponent> Children => _mainContainer.Children;

    public Collapsible(string title, float iconSize = 20f)
    {
        _collapsedIcon = new SvgImage(ResourceLoader.GetSvg("arrow_drop_right"), iconSize, iconSize);
        _expandedIcon  = new SvgImage(ResourceLoader.GetSvg("arrow_drop_down"),  iconSize, iconSize);

        _headerButton = new Button.Button(title)
        {
            Icon = ButtonIcon.Left,
            IconImage = _collapsedIcon,
            HorizontalSize = SizeMode.Expand,
            ContentAlign = HAlign.Left,
            CornerRadius = 0f
        };

        _headerButton.Click += Toggle;

        _content = new VStack();
        _mainContainer = new VStack();

        _mainContainer.Parent = this;
        _mainContainer.AddComponent(_headerButton);
        _mainContainer.AddComponent(_content);

        _isExpanded = false;
        _content.Present = false;
    }

    public void Toggle()
    {
        IsExpanded = !IsExpanded;
        Toggled?.Invoke();
    }

    public void AddComponent(IComponent child) => _content.AddComponent(child);

    public void AddComponents(params IComponent[] children) => _content.AddComponents(children);

    // --------------------------------------------------------
    //  Layout - delegate to MainContainer
    // --------------------------------------------------------

    protected override SKSize MeasureContent(SKSize availableSize)
    {
        _mainContainer.HorizontalSize = HorizontalSize;
        _mainContainer.VerticalSize = VerticalSize;
        _content.HorizontalSize = HorizontalSize;
        _content.VerticalSize = VerticalSize;

        return _mainContainer.Measure(availableSize);
    }

    protected override void ArrangeContent(SKRect contentBounds)
    {
        _mainContainer.Arrange(contentBounds);
    }

    protected override void ResolveContent()
    {
        _mainContainer.Resolve();
    }

    protected override void RenderContent(SKCanvas canvas, SKRect contentBounds)
    {
        _mainContainer.Render(canvas);
    }

    // --------------------------------------------------------
    //  Dispose
    // --------------------------------------------------------

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _headerButton.Click -= Toggle;
            _mainContainer.Dispose();
            _collapsedIcon.Dispose();
            _expandedIcon.Dispose();
        }

        base.Dispose(disposing);
    }
}