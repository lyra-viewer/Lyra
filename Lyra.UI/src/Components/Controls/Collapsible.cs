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

    // True when the header is a caller-supplied component (chevron lives in the header row) rather
    // than a text title (chevron is the button's left icon).
    private readonly bool _customHeader;

    private bool _isExpanded;

    public bool IsExpanded
    {
        get => _isExpanded;
        set
        {
            if (_isExpanded == value)
                return;

            _isExpanded = value;
            _content.Present = value;

            if (_customHeader)
            {
                // Both chevrons sit in the header row; toggle which one shows.
                _collapsedIcon.Present = !value;
                _expandedIcon.Present = value;
            }
            else
            {
                _headerButton.IconImage = value ? _expandedIcon : _collapsedIcon;
            }

            Invalidate();
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
    public IReadOnlyList<IComponent> Children => _mainContainer.Children;

    /// <summary>A section-style collapsible with a left arrow and a text title.</summary>
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

        _content = new VStack();
        _mainContainer = BuildContainer();
    }

    /// <summary>
    /// A collapsible with a caller-supplied header component. The header keeps the button's full
    /// behaviour (background, hover, press, click); a chevron is appended on the right and kept in
    /// sync with the expanded state.
    /// </summary>
    public Collapsible(IComponent header, float chevronSize = 18f)
    {
        _customHeader = true;

        _collapsedIcon = new SvgImage(ResourceLoader.GetSvg("arrow_drop_right"), chevronSize, chevronSize);
        _expandedIcon  = new SvgImage(ResourceLoader.GetSvg("arrow_drop_down"),  chevronSize, chevronSize) { Present = false };

        header.HorizontalSize = SizeMode.Expand; // push the chevron to the right edge

        var row = new HStack
        {
            Spacing = 4,
            HorizontalSize = SizeMode.Expand,
            VerticalAlign = VAlign.Center
        };
        row.AddComponents(header, _collapsedIcon, _expandedIcon);

        _headerButton = new Button.Button
        {
            Content = row,
            HorizontalSize = SizeMode.Expand,
            ContentAlign = HAlign.Left,
            CornerRadius = 0f
        };

        _content = new VStack();
        _mainContainer = BuildContainer();
    }

    private VStack BuildContainer()
    {
        _headerButton.Click += Toggle;

        var main = new VStack { Parent = this };
        main.AddComponent(_headerButton);
        main.AddComponent(_content);

        _isExpanded = false;
        _content.Present = false;
        return main;
    }

    public void Toggle()
    {
        IsExpanded = !IsExpanded;
        Toggled?.Invoke();
    }

    public void AddComponent(IComponent child) => _content.AddComponent(child);

    public void AddComponents(params IComponent[] children) => _content.AddComponents(children);

    // Children exposes the main container's children rather than the container
    // itself, so the base propagation skips it.
    protected override void PropagateContext(UIContext? context)
    {
        base.PropagateContext(context);
        _mainContainer.Context = context;
    }

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

            // Text mode: the button references but does not own its icons, so dispose them here.
            // Custom mode: the chevrons live in the header row and are disposed via the button.
            if (!_customHeader)
            {
                _collapsedIcon.Dispose();
                _expandedIcon.Dispose();
            }

            _mainContainer.Dispose();
        }

        base.Dispose(disposing);
    }
}
