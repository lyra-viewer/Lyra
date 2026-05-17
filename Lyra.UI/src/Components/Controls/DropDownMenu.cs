using Lyra.UI.Components.Controls.Button;
using Lyra.UI.Components.Layout;
using Lyra.UI.Components.Primitives;
using Lyra.UI.SupportingTypes;
using Lyra.UI.Theme;
using SkiaSharp;

namespace Lyra.UI.Components.Controls;

/// <summary>
/// A single-select dropdown that opens a floating popup of options
/// above the main UI when its header is clicked. Picking an option
/// updates <see cref="Selected"/>, fires <see cref="SelectionChanged"/>,
/// and dismisses the popup.
///
/// The options panel is hosted by an <see cref="IPopupHost"/> while
/// open, but ownership stays with the dropdown — it disposes the
/// panel and its menu items.
///
/// TODO: if an ancestor is hidden (e.g. the sidebar collapses) while
/// the popup is open, the popup orphans. Acceptable for now; would
/// need ancestor-visibility tracking to fix cleanly.
/// </summary>
public class DropDownMenu<T> : ComponentBase, IContainer
    where T : notnull
{
    private readonly IPopupHost _popupHost;
    private readonly Func<T, string> _displayName;
    private readonly Func<T, string> _headerDisplay;

    private readonly Button.Button _headerButton;
    private readonly VStack _optionsPanel;
    private readonly List<(T Value, MenuItem Item)> _items = [];

    private readonly SvgImage _collapsedIcon;
    private readonly SvgImage _expandedIcon;

    private readonly IComponent[] _children;

    private bool _isOpen;
    private T _selected;

    /// Fired when the user picks an option.
    /// Not raised when <see cref="Selected"/> is set programmatically.
    public event Action<T>? SelectionChanged;

    public T Selected
    {
        get => _selected;
        set => SetSelected(value, fireEvent: false);
    }

    // The header is the only in-tree child; the options panel
    // lives in the popup layer when open.
    public IReadOnlyList<IComponent> Children => _children;

    public DropDownMenu(
        IPopupHost popupHost,
        IEnumerable<T> values,
        Func<T, string> displayName,
        T initialSelection,
        Func<T, string>? headerDisplay = null,
        float iconSize = 20f)
    {
        ArgumentNullException.ThrowIfNull(popupHost);
        ArgumentNullException.ThrowIfNull(values);
        ArgumentNullException.ThrowIfNull(displayName);

        _popupHost = popupHost;
        _displayName = displayName;
        _headerDisplay = headerDisplay ?? displayName;
        _selected = initialSelection;

        _collapsedIcon = new SvgImage(ResourceLoader.GetSvg("arrow_drop_right"), iconSize, iconSize);
        _expandedIcon = new SvgImage(ResourceLoader.GetSvg("arrow_drop_down"), iconSize, iconSize);

        _headerButton = new Button.Button(_headerDisplay(initialSelection), ButtonVariant.Outline)
        {
            Icon = ButtonIcon.Left,
            IconImage = _collapsedIcon,
            HorizontalSize = SizeMode.Expand,
            ContentAlign = HAlign.Left,
            CornerRadius = 0f
        };
        _headerButton.Parent = this;
        _headerButton.Click += Toggle;

        _optionsPanel = new VStack
        {
            BackgroundColor = Palette.Panel,
            VerticalSize = SizeMode.Shrink
        };

        foreach (var value in values)
        {
            var capturedValue = value;
            var item = new MenuItem(displayName(value))
            {
                HorizontalSize = SizeMode.Expand,
                IsPicked = EqualityComparer<T>.Default.Equals(value, initialSelection)
            };
            item.Click += () => PickValue(capturedValue);

            _items.Add((value, item));
            _optionsPanel.AddComponent(item);
        }

        _children = [_headerButton];
    }

    // --------------------------------------------------------
    //  Open / dismiss
    // --------------------------------------------------------

    public void Toggle()
    {
        if (_isOpen)
            _popupHost.DismissPopup();
        else
            Open();
    }

    private void Open()
    {
        var headerBounds = _headerButton.ArrangedBounds;
        var position = new SKPoint(headerBounds.Left, headerBounds.Bottom);

        // Pin popup width to the header so it reads as part of the
        // dropdown rather than a floating menu of arbitrary size.
        _optionsPanel.HorizontalSize = SizeMode.Fixed;
        _optionsPanel.Width = headerBounds.Width;

        _isOpen = true;
        _headerButton.IconImage = _expandedIcon;

        _popupHost.ShowPopup(_optionsPanel, position, OnDismissed);
    }

    private void OnDismissed()
    {
        _isOpen = false;
        _headerButton.IconImage = _collapsedIcon;
    }

    // --------------------------------------------------------
    //  Selection
    // --------------------------------------------------------

    private void PickValue(T value)
    {
        SetSelected(value, fireEvent: true);
        _popupHost.DismissPopup();
    }

    private void SetSelected(T value, bool fireEvent)
    {
        _selected = value;
        _headerButton.Text = _headerDisplay(value);

        foreach (var (v, item) in _items)
            item.IsPicked = EqualityComparer<T>.Default.Equals(v, value);

        if (fireEvent)
            SelectionChanged?.Invoke(value);
    }

    // --------------------------------------------------------
    //  IContainer - options are fixed at construction
    // --------------------------------------------------------

    public void AddComponent(IComponent child) =>
        throw new NotSupportedException("DropDownMenu options are defined via its constructor.");

    public void AddComponents(params IComponent[] children) =>
        throw new NotSupportedException("DropDownMenu options are defined via its constructor.");

    // --------------------------------------------------------
    //  Layout - delegate to header (popup lives in popup layer)
    // --------------------------------------------------------

    protected override SKSize MeasureContent(SKSize availableSize)
    {
        return _headerButton.Measure(availableSize);
    }

    protected override void ResolveContent()
    {
        _headerButton.Resolve();
    }

    protected override void ArrangeContent(SKRect contentBounds)
    {
        _headerButton.Arrange(contentBounds);
    }

    protected override void RenderContent(SKCanvas canvas, SKRect contentBounds)
    {
        _headerButton.Render(canvas);
    }

    // --------------------------------------------------------
    //  Dispose
    // --------------------------------------------------------

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            // Don't leave the popup host holding a reference to a
            // panel we're about to dispose.
            if (_isOpen)
                _popupHost.DismissPopup();

            _headerButton.Click -= Toggle;
            _headerButton.Dispose();
            _optionsPanel.Dispose();
            _collapsedIcon.Dispose();
            _expandedIcon.Dispose();
        }

        base.Dispose(disposing);
    }
}