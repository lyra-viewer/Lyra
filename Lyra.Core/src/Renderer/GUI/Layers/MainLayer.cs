using Lyra.FileLoader;
using Lyra.Renderer.GUI.Sections;
using Lyra.Renderer.GUI.Support;
using Lyra.UI;
using Lyra.UI.Components.Controls;
using Lyra.UI.Components.Layout;
using Lyra.UI.SupportingTypes;
using Lyra.UI.Theme;

namespace Lyra.Renderer.GUI.Layers;

// ============================================================================
//  MainLayer - primary UI tree (info pane, sidebar, all sections)
// ----------------------------------------------------------------------------
//  Owns all UI sections, the sidebar, and the key-column registry.
//  Builds the component tree and adds it as the "Main" layer.
//
//  Refresh() pushes UIState to all sections and updates visibility.
//  Debug helpers expose the debug section for input diagnostics
//  without leaking section internals to UIManager.
// ============================================================================
public class MainLayer : IDisposable
{
    private readonly UIContext _context;
    private readonly KeyColumnRegistry _keyColumnRegistry = new();

    // Sections
    private readonly InfoSection _infoSection;
    private readonly MenuSection _menuSection;
    private readonly DirectoryTreeSection _directoryTreeSection;
    private readonly ExifSection _exifSection;
    private readonly FormatSection _formatSection;
    private readonly LayersSection _layersSection;
    private readonly HelpSection _helpSection;
    private readonly DebugSection _debugSection;

    private readonly IUISection[] _sections;

    private readonly VStack _sidebar;

    private UIState? _lastState;

    public Layer Layer { get; }

    /// <summary>
    /// Fired when the user picks a directory in the sidebar tree.
    /// Parameter is the normalized absolute directory path.
    /// </summary>
    public event Action<string>? DirectoryPicked;

    public MainLayer(UIContext context)
    {
        _context = context;

        _infoSection = new InfoSection();
        _menuSection = new MenuSection();
        _directoryTreeSection = new DirectoryTreeSection();
        _exifSection = new ExifSection(_keyColumnRegistry);
        _formatSection = new FormatSection(_keyColumnRegistry);
        _layersSection = new LayersSection();
        _helpSection = new HelpSection();
        _debugSection = new DebugSection();

        // Cross-section wiring
        _menuSection.ButtonClicked += text => _debugSection.SetAction(text);
        _directoryTreeSection.DirectoryPicked += path => DirectoryPicked?.Invoke(path);

        _sections =
        [
            _infoSection,
            _menuSection,
            _directoryTreeSection,
            _exifSection,
            _formatSection,
            _layersSection,
            _helpSection,
            _debugSection
        ];

        _sidebar = BuildSidebar();
        Layer = BuildLayer();
    }

    // --------------------------------------------------------
    //  State refresh
    // --------------------------------------------------------

    public void Refresh(UIState state)
    {
        if (_lastState is not null && _lastState == state)
            return;

        _lastState = state;
        RefreshInternal(state);
    }

    /// <summary>
    /// Re-runs section refresh without the UIState equality short-circuit.
    /// Use when backing data behind the current state changed but the state
    /// snapshot itself did not (e.g. tile decode progress).
    /// </summary>
    public void RefreshCurrent()
    {
        if (_lastState is null)
            return;

        RefreshInternal(_lastState);
    }

    private void RefreshInternal(UIState state)
    {
        _infoSection.Root.Present = state.AppStates.InfoVisible;
        _helpSection.Root.Present = state.AppStates.HelpVisible;
        _sidebar.Present = state.AppStates.SidebarVisible;
        _directoryTreeSection.Root.Present = state.AppStates.CollectionType is CollectionType.MultiDirectorySelection;

        _keyColumnRegistry.BeginFrame();

        foreach (var section in _sections)
            section.Refresh(state);

        _context.Invalidate();
    }

    // --------------------------------------------------------
    //  Debug helpers
    // --------------------------------------------------------

    public void SetDebugPointer(float x, float y) => _debugSection.SetPointer(x, y);

    public void SetDebugHit(string description) => _debugSection.SetHit(description);

    // --------------------------------------------------------
    //  Tree construction
    // --------------------------------------------------------

    private Layer BuildLayer()
    {
        // Left pane: info rows on top, empty content area in the middle,
        // shortcuts at the bottom.
        var contentArea = new VStack
        {
            HorizontalSize = SizeMode.Expand,
            VerticalSize = SizeMode.Expand,
            Transient = true
        };

        var leftPane = new VStack
        {
            HorizontalSize = SizeMode.Expand,
            VerticalSize = SizeMode.Expand,
            Transient = true
        };
        leftPane.AddComponents(_infoSection.Root, contentArea, _helpSection.Root);

        // Root: left pane on the left, sidebar on the right.
        var root = new HStack
        {
            HorizontalSize = SizeMode.Expand,
            VerticalSize = SizeMode.Expand,
            Transient = true
        };
        root.AddComponents(leftPane, _sidebar);

        var layer = _context.AddLayer("Main");
        layer.Root = root;
        return layer;
    }

    private VStack BuildSidebar()
    {
        // Data-bearing collapsibles participate in surplus distribution
        // when expanded, but collapse to header-only when closed.
        Collapsible[] flexibleCollapsibles =
        [
            _directoryTreeSection.Collapsible,
            _exifSection.Collapsible,
            _formatSection.Collapsible,
            _layersSection.Collapsible
        ];

        foreach (var c in flexibleCollapsibles)
        {
            c.VerticalSize = c.IsExpanded ? SizeMode.Flexible : SizeMode.Shrink;
            c.Toggled += () => c.VerticalSize = c.IsExpanded ? SizeMode.Flexible : SizeMode.Shrink;
        }

        var sidebar = new VStack
        {
            HorizontalSize = SizeMode.Fixed,
            VerticalSize = SizeMode.Expand,
            VerticalAlign = VAlign.Top,
            Width = 300,
            MinWidth = 200,
            MaxWidth = 800,
            ResizeEdges = ResizeEdge.Left,
            Spacing = SidebarSpacing,
            Padding = new Padding(8),
            BackgroundColor = Palette.Panel
        };
        sidebar.AddComponents(
            _menuSection.Root,
            _directoryTreeSection.Root,
            _exifSection.Root,
            _formatSection.Root,
            _layersSection.Root,
            _debugSection.Root
        );

        return sidebar;
    }

    private const float SidebarSpacing = 4f;

    // --------------------------------------------------------
    //  Dispose
    // --------------------------------------------------------

    public void Dispose()
    {
        foreach (var section in _sections)
        {
            if (section is IDisposable disposable)
                disposable.Dispose();
        }

        // Component tree disposed by Layer via UIContext.Dispose.
    }
}