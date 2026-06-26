using Lyra.Common.Settings;
using Lyra.Common.Settings.Enums;
using Lyra.Common.SystemExtensions;
using Lyra.UI;
using Lyra.UI.Components;
using Lyra.UI.Components.Controls;
using Lyra.UI.Components.Controls.Button;
using Lyra.UI.Components.Layout;
using Lyra.UI.SupportingTypes;

namespace Lyra.Renderer.GUI.Sections;

public sealed class MenuSection : IUISection
{
    private readonly DropDownMenu<InitDisplayMode> _initDisplayModeDropdown;
    private readonly DropDownMenu<BackgroundMode> _backgroundDropdown;
    private readonly DropDownMenu<SamplingMode> _samplingDropdown;

    public Collapsible Collapsible { get; }

    public IComponent Root => Collapsible;

    public event Action? OpenFileClicked;
    public event Action? OpenDirectoryClicked;
    public event Action? FullscreenClicked;
    public event Action? QuitClicked;
    public event Action? ShowDuplicatesFinderClicked;
    public event Action<InitDisplayMode>? InitDisplayModeChanged;
    public event Action<BackgroundMode>? BackgroundModeChanged;
    public event Action<SamplingMode>? SamplingModeChanged;

    public MenuSection(IPopupHost popupHost)
    {
        var openFileButton = new Button("OPEN")
        {
            CornerRadius = 0f,
            HorizontalSize = SizeMode.Expand,
        };
        openFileButton.Click += () => OpenFileClicked?.Invoke();

        var openDirButton = new Button("OPEN DIR")
        {
            CornerRadius = 0f,
            HorizontalSize = SizeMode.Expand
        };
        openDirButton.Click += () => OpenDirectoryClicked?.Invoke();

        var openRow = new HStack
        {
            HorizontalSize = SizeMode.Expand,
            Spacing = 4f,
            Transient = true,
            Padding = new Padding(0, 4f, 0, 0f)
        };
        openRow.AddComponents(openFileButton, openDirButton);

        var fullscreenButton = new Button("FULL SCREEN")
        {
            CornerRadius = 0f,
            HorizontalSize = SizeMode.Expand
        };
        fullscreenButton.Click += () => FullscreenClicked?.Invoke();

        var quitButton = new Button("QUIT", ButtonVariant.Danger)
        {
            CornerRadius = 0f,
            HorizontalSize = SizeMode.Expand
        };
        quitButton.Click += () => QuitClicked?.Invoke();

        var quitRow = new HStack
        {
            HorizontalSize = SizeMode.Expand,
            Spacing = 4f,
            Transient = true,
            Padding = new Padding(0, 4f, 0, 0)
        };
        quitRow.AddComponents(fullscreenButton, quitButton);
        
        var duplicatesFinderButton = new Button("DUPLICATES FINDER")
        {
            CornerRadius = 0f,
            HorizontalSize = SizeMode.Expand
        };
        duplicatesFinderButton.Click += () => ShowDuplicatesFinderClicked?.Invoke();

        var duplicatesFinderRow = new HStack
        {
            HorizontalSize = SizeMode.Expand,
            Spacing = 4f,
            Transient = true,
            Padding = new Padding(0, 4f, 0, 0)
        };
        duplicatesFinderRow.AddComponents(duplicatesFinderButton);

        _initDisplayModeDropdown = new DropDownMenu<InitDisplayMode>(
            popupHost,
            Enum.GetValues<InitDisplayMode>(),
            m => m.Description(),
            SettingsManager.UiSettings.InitDisplayMode,
            headerDisplay: m => $"INIT DISPLAY MODE: {m.Description()}")
        {
            HorizontalSize = SizeMode.Expand,
            Padding = new Padding(0, 4f, 0, 0)
        };
        _initDisplayModeDropdown.SelectionChanged += mode => InitDisplayModeChanged?.Invoke(mode);

        _backgroundDropdown = new DropDownMenu<BackgroundMode>(
            popupHost,
            Enum.GetValues<BackgroundMode>(),
            m => m.Description(),
            SettingsManager.UiSettings.BackgroundMode,
            headerDisplay: m => $"BACKGROUND: {m.Description()}")
        {
            HorizontalSize = SizeMode.Expand,
            Padding = new Padding(0, 4f, 0, 0)
        };
        _backgroundDropdown.SelectionChanged += mode => BackgroundModeChanged?.Invoke(mode);

        _samplingDropdown = new DropDownMenu<SamplingMode>(
            popupHost,
            Enum.GetValues<SamplingMode>(),
            m => m.Description(),
            SettingsManager.UiSettings.SamplingMode,
            headerDisplay: m => $"SAMPLING: {m.Description()}")
        {
            HorizontalSize = SizeMode.Expand,
            Padding = new Padding(0, 4f, 0, 0f)
        };
        _samplingDropdown.SelectionChanged += mode => SamplingModeChanged?.Invoke(mode);

        var bottomSeparator = new HStack
        {
            HorizontalSize = SizeMode.Expand,
            Transient = true,
            Padding = new Padding(0, 0, 0, 12f)
        };

        var collapsible = new Collapsible("MENU")
        {
            HorizontalSize = SizeMode.Expand
        };
        collapsible.AddComponents(openRow, quitRow, duplicatesFinderRow, _initDisplayModeDropdown, _backgroundDropdown, _samplingDropdown, bottomSeparator);
        Collapsible = collapsible;
    }

    public void SetInitDisplayMode(InitDisplayMode mode) => _initDisplayModeDropdown.Selected = mode;

    public void SetBackgroundMode(BackgroundMode mode) => _backgroundDropdown.Selected = mode;

    public void SetSamplingMode(SamplingMode mode) => _samplingDropdown.Selected = mode;

    public void Refresh(UIState state)
    {
        // Menu content is user-driven; nothing to refresh.
    }
}