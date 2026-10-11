using Lyra.UI.SupportingTypes;
using SkiaSharp;

namespace Lyra.UI.Components.Controls;

/// <summary>
/// A pointer-anchored menu with one level of cascading submenus,
/// shown through an <see cref="IPopupHost"/>.
/// </summary>
public sealed class ContextMenu : ComponentBase, IContainer
{
    private const float SubmenuOverlap = 2f;

    private readonly IPopupHost _popupHost;
    private readonly MenuPanel _rootPanel = new();
    private readonly List<ContextSubmenu> _submenus = [];
    private readonly List<IComponent> _children;

    private ContextSubmenu? _openSubmenu;
    private SKPoint _anchor;

    public bool IsOpen { get; private set; }

    public IReadOnlyList<IComponent> Children => _children;

    public ContextMenu(IPopupHost popupHost)
    {
        ArgumentNullException.ThrowIfNull(popupHost);

        _popupHost = popupHost;
        _children = [_rootPanel];
        _rootPanel.Parent = this;

        HorizontalSize = SizeMode.Expand;
        VerticalSize = SizeMode.Expand;
        Transient = true;
    }

    // --------------------------------------------------------
    //  Building
    // --------------------------------------------------------

    /// <summary>Adds a command row. Picking it dismisses the menu, then runs <paramref name="onClick"/>.</summary>
    public MenuItem AddItem(string text, Action onClick, string? shortcut = null)
    {
        var item = new MenuItem(text, shortcut);
        item.Click += () => Pick(onClick);
        item.Hovered += CloseSubmenu;

        _rootPanel.AddComponent(item);
        return item;
    }

    /// <summary>Adds a row that opens a submenu on hover or click.</summary>
    public ContextSubmenu AddSubmenu(string text, string? shortcut = null)
    {
        var header = new MenuItem(text, shortcut, hasSubmenu: true);
        var submenu = new ContextSubmenu(this, header);

        header.Hovered += () => OpenSubmenu(submenu);
        header.Click += () => OpenSubmenu(submenu);

        submenu.Panel.Present = false;
        submenu.Panel.Parent = this;

        _rootPanel.AddComponent(header);
        _submenus.Add(submenu);
        _children.Add(submenu.Panel);

        return submenu;
    }

    public void AddSeparator() => _rootPanel.AddComponent(new MenuSeparator());

    // --------------------------------------------------------
    //  Open / dismiss
    // --------------------------------------------------------

    /// <summary>Shows the menu with its top-left corner at <paramref name="position"/>, replacing any open popup.</summary>
    public void Open(SKPoint position)
    {
        _popupHost.ShowPopup(this, SKPoint.Empty, OnDismissed);

        _anchor = position;
        IsOpen = true;
        Invalidate();
    }

    public void Dismiss()
    {
        if (IsOpen)
            _popupHost.DismissPopup();
    }

    private void OnDismissed()
    {
        IsOpen = false;
        CloseSubmenu();
        ResetItems(_rootPanel);

        foreach (var submenu in _submenus)
            ResetItems(submenu.Panel);
    }

    internal void Pick(Action onClick)
    {
        Dismiss();
        onClick();
    }

    private static void ResetItems(MenuPanel panel)
    {
        foreach (var child in panel.Children)
        {
            if (child is MenuItem item)
                item.ResetInteraction();
        }
    }

    // --------------------------------------------------------
    //  Submenus
    // --------------------------------------------------------

    private void OpenSubmenu(ContextSubmenu submenu)
    {
        if (ReferenceEquals(_openSubmenu, submenu))
            return;

        CloseSubmenu();

        _openSubmenu = submenu;
        submenu.Header.IsHighlighted = true;
        submenu.Panel.Present = true;
    }

    private void CloseSubmenu()
    {
        if (_openSubmenu is null)
            return;

        _openSubmenu.Header.IsHighlighted = false;
        _openSubmenu.Panel.Present = false;
        _openSubmenu = null;
    }

    // --------------------------------------------------------
    //  IContainer - rows are added through AddItem / AddSubmenu
    // --------------------------------------------------------

    public void AddComponent(IComponent child) =>
        throw new NotSupportedException("ContextMenu rows are added through AddItem / AddSubmenu.");

    public void AddComponents(params IComponent[] children) =>
        throw new NotSupportedException("ContextMenu rows are added through AddItem / AddSubmenu.");

    // --------------------------------------------------------
    //  Layout
    // --------------------------------------------------------

    protected override SKSize MeasureContent(SKSize availableSize)
    {
        _rootPanel.Measure(availableSize);

        foreach (var submenu in _submenus)
            submenu.Panel.Measure(availableSize);

        return availableSize;
    }

    protected override void ArrangeContent(SKRect contentBounds)
    {
        var rootSize = _rootPanel.DesiredSize;

        // Flip to the left of / above the pointer when there is no room,
        // the way native context menus do near the window edges.
        var x = _anchor.X + rootSize.Width > contentBounds.Right ? _anchor.X - rootSize.Width : _anchor.X;
        var y = _anchor.Y + rootSize.Height > contentBounds.Bottom ? _anchor.Y - rootSize.Height : _anchor.Y;

        var rootRect = Overlay.Place(x, y, rootSize, contentBounds);
        _rootPanel.Arrange(rootRect);

        foreach (var submenu in _submenus)
        {
            if (!submenu.Panel.Present)
                continue;

            var size = submenu.Panel.DesiredSize;
            var header = submenu.Header.ArrangedBounds;

            var subX = rootRect.Right - SubmenuOverlap + size.Width > contentBounds.Right
                ? rootRect.Left + SubmenuOverlap - size.Width
                : rootRect.Right - SubmenuOverlap;

            var subY = header.Top - MenuPanel.PanelPadding;

            submenu.Panel.Arrange(Overlay.Place(subX, subY, size, contentBounds));
        }
    }

    protected override void ResolveContent()
    {
        _rootPanel.Resolve();

        foreach (var submenu in _submenus)
            submenu.Panel.Resolve();
    }

    protected override void RenderContent(SKCanvas canvas, SKRect contentBounds)
    {
        _rootPanel.Render(canvas);

        if (_openSubmenu is not null)
            _openSubmenu.Panel.Render(canvas);
    }

    // --------------------------------------------------------
    //  Dispose
    // --------------------------------------------------------

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            if (IsOpen)
                _popupHost.DismissPopup();

            _rootPanel.Dispose();

            foreach (var submenu in _submenus)
                submenu.Panel.Dispose();
        }

        base.Dispose(disposing);
    }
}

/// <summary>
/// A submenu of a <see cref="ContextMenu"/>, returned by
/// <see cref="ContextMenu.AddSubmenu"/>. Its rows belong to the menu.
/// </summary>
public sealed class ContextSubmenu
{
    private readonly ContextMenu _menu;

    /// <summary>The row in the parent menu that opens this submenu.</summary>
    public MenuItem Header { get; }
    internal MenuPanel Panel { get; } = new();

    internal ContextSubmenu(ContextMenu menu, MenuItem header)
    {
        _menu = menu;
        Header = header;
    }

    public MenuItem AddItem(string text, Action onClick, string? shortcut = null)
    {
        var item = new MenuItem(text, shortcut);
        item.Click += () => _menu.Pick(onClick);

        Panel.AddComponent(item);
        return item;
    }

    public void AddSeparator() => Panel.AddComponent(new MenuSeparator());
}
