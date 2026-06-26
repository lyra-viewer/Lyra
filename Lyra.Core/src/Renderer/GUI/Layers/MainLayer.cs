using Lyra.Common.Settings.Enums;
using Lyra.FileLoader.Enumeration;
using Lyra.Renderer.GUI.Sections;
using Lyra.Renderer.GUI.Support;
using Lyra.UI;
using Lyra.UI.Components;
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
//
//  Layout is driven by a single _entries table that declares each
//  section's placement (LeftPane vs Sidebar) and whether its
//  Collapsible should flex with available height.
// ============================================================================
public class MainLayer : IUIEvents, IDisposable
{
    private const float SidebarSpacing = 4f;

    private readonly UIContext _context;
    private readonly KeyColumnRegistry _keyColumnRegistry = new();

    // Sections
    private readonly InfoSection _infoSection;
    private readonly MenuSection _menuSection;
    private readonly DuplicatesFinderSection _duplicatesFinderSection;
    private readonly DirectoryTreeSection _directoryTreeSection;
    private readonly ExifSection _exifSection;
    private readonly FormatSection _formatSection;
    private readonly StructureSection _structureSection;
    private readonly LayersSection _layersSection;
    private readonly HelpSection _helpSection;
    private readonly DebugSection _debugSection;

    private readonly SectionEntry[] _entries;
    private readonly VStack _sidebar;

    private UIState? _lastState;

    public Layer Layer { get; }

    public event Action? OpenFileRequested;
    public event Action? OpenDirectoryRequested;
    public event Action? FullscreenRequested;
    public event Action? QuitRequested;
    public event Action? FindDuplicatesRequested;
    public event Action? DuplicatesGoBackRequested;
    public event Action<bool>? DuplicatesExactOnlyChanged;
    public event Action<int>? DuplicatesHashToleranceChanged;
    public event Action<string>? DirectoryPicked;
    public event Action<InitDisplayMode>? InitDisplayModeChanged;
    public event Action<BackgroundMode>? BackgroundModeChanged;
    public event Action<SamplingMode>? SamplingModeChanged;

    public MainLayer(UIContext context)
    {
        _context = context;

        _infoSection = new InfoSection();
        _menuSection = new MenuSection(_context);
        _duplicatesFinderSection = new DuplicatesFinderSection();
        _directoryTreeSection = new DirectoryTreeSection();
        _exifSection = new ExifSection(_keyColumnRegistry);
        _formatSection = new FormatSection(_keyColumnRegistry);
        _structureSection = new StructureSection(_keyColumnRegistry);
        _layersSection = new LayersSection();
        _helpSection = new HelpSection();
        _debugSection = new DebugSection();

        // Cross-section wiring
        _menuSection.OpenFileClicked += () => OnMenu("OPEN", OpenFileRequested);
        _menuSection.OpenDirectoryClicked += () => OnMenu("OPEN DIR", OpenDirectoryRequested);
        _menuSection.FullscreenClicked += () => OnMenu("FULL SCREEN", FullscreenRequested);
        _menuSection.QuitClicked += () => OnMenu("QUIT", QuitRequested);
        _menuSection.ShowDuplicatesFinderClicked += () => { _debugSection.SetAction("DUPLICATES FINDER"); _duplicatesFinderSection.Show(); _context.Invalidate(); };
        _menuSection.InitDisplayModeChanged += mode => OnMenu("INIT DISPLAY MODE", InitDisplayModeChanged, mode);
        _menuSection.BackgroundModeChanged += mode => OnMenu("BACKGROUND", BackgroundModeChanged, mode);
        _menuSection.SamplingModeChanged += mode => OnMenu("SAMPLING", SamplingModeChanged, mode);
        _directoryTreeSection.DirectoryPicked += path => DirectoryPicked?.Invoke(path);

        _duplicatesFinderSection.FindClicked += () => OnMenu("FIND DUPLICATES", FindDuplicatesRequested);
        _duplicatesFinderSection.GoBackClicked += () => OnMenu("DUPLICATES BACK", DuplicatesGoBackRequested);
        _duplicatesFinderSection.CloseClicked += () => { _debugSection.SetAction("DUPLICATES CLOSE"); _duplicatesFinderSection.Hide(); _context.Invalidate(); };
        _duplicatesFinderSection.ExactCopiesOnlyChanged += v => DuplicatesExactOnlyChanged?.Invoke(v);
        _duplicatesFinderSection.ToleranceChanged += v => DuplicatesHashToleranceChanged?.Invoke(v);

        // Single source of truth for section layout.
        // Order within Sidebar entries determines vertical display order.
        _entries =
        [
            new SectionEntry(_infoSection, SectionPlacement.LeftPane),
            new SectionEntry(_menuSection, SectionPlacement.Sidebar),
            new SectionEntry(_duplicatesFinderSection, SectionPlacement.Sidebar),
            new SectionEntry(_directoryTreeSection, SectionPlacement.Sidebar, _directoryTreeSection.Collapsible),
            new SectionEntry(_exifSection, SectionPlacement.Sidebar, _exifSection.Collapsible),
            new SectionEntry(_formatSection, SectionPlacement.Sidebar, _formatSection.Collapsible),
            new SectionEntry(_structureSection, SectionPlacement.Sidebar, _structureSection.Collapsible),
            new SectionEntry(_layersSection, SectionPlacement.Sidebar, _layersSection.Collapsible),
            new SectionEntry(_debugSection, SectionPlacement.Sidebar),
            new SectionEntry(_helpSection, SectionPlacement.LeftPane),
        ];
        
#if DEBUG
        _debugSection.Root.Present = true;
#else
        _debugSection.Root.Present = SettingsManager.AppSettings.Debug;
#endif

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

        foreach (var entry in _entries)
            entry.Section.Refresh(state);

        _context.Invalidate();
    }

    // --------------------------------------------------------
    //  Menu event helpers
    // --------------------------------------------------------

    private void OnMenu(string action, Action? invoke)
    {
        _debugSection.SetAction(action);
        invoke?.Invoke();
    }

    private void OnMenu<T>(string actionPrefix, Action<T>? invoke, T value)
    {
        _debugSection.SetAction($"{actionPrefix} {value}");
        invoke?.Invoke(value);
    }

    // --------------------------------------------------------
    //  Programmatic dropdown sync (driven by keyboard toggles)
    // --------------------------------------------------------

    public void SetInitDisplayMode(InitDisplayMode mode) => _menuSection.SetInitDisplayMode(mode);

    public void SetBackgroundMode(BackgroundMode mode) => _menuSection.SetBackgroundMode(mode);

    public void SetSamplingMode(SamplingMode mode) => _menuSection.SetSamplingMode(mode);

    public void SetDuplicatesState(bool inDuplicatesMode, bool noDuplicatesFound)
    {
        _duplicatesFinderSection.SetState(inDuplicatesMode, noDuplicatesFound);
        _context.Invalidate();
    }

    // --------------------------------------------------------
    //  Debug helpers
    // --------------------------------------------------------

    public void SetDebugPointer(float x, float y) => _debugSection.SetPointer(x, y);

    public void SetDebugHover(IComponent? hovered) => _debugSection.SetHover(hovered);

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
        var sidebarEntries = _entries
            .Where(e => e.Placement == SectionPlacement.Sidebar)
            .ToArray();

        // Data-bearing collapsibles participate in surplus distribution
        // when expanded, but collapse to header-only when closed.
        foreach (var entry in sidebarEntries)
        {
            if (entry.Flexible is not { } c)
                continue;

            c.VerticalSize = c.IsExpanded ? SizeMode.Flexible : SizeMode.Shrink;
            c.Toggled += () => c.VerticalSize = c.IsExpanded ? SizeMode.Flexible : SizeMode.Shrink;
        }

        var sidebar = new VStack
        {
            HorizontalSize = SizeMode.Fixed,
            VerticalSize = SizeMode.Expand,
            VerticalAlign = VAlign.Top,
            Width = 350,
            MinWidth = 200,
            MaxWidth = 800,
            ResizeEdges = ResizeEdge.Left,
            Spacing = SidebarSpacing,
            Padding = new Padding(4),
            BackgroundColor = Palette.Panel
        };
        sidebar.AddComponents(sidebarEntries.Select(e => e.Section.Root).ToArray());

        return sidebar;
    }

    // --------------------------------------------------------
    //  Dispose
    // --------------------------------------------------------

    public void Dispose()
    {
        foreach (var entry in _entries)
        {
            if (entry.Section is IDisposable disposable)
                disposable.Dispose();
        }

        // Component tree disposed by Layer via UIContext.Dispose.
    }

    // --------------------------------------------------------
    //  Layout descriptor types
    // --------------------------------------------------------

    private enum SectionPlacement
    {
        LeftPane,
        Sidebar
    }

    private record SectionEntry(IUISection Section, SectionPlacement Placement, Collapsible? Flexible = null);
}