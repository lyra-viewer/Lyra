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
    private readonly DropDownMenu<BackgroundMode> _backgroundDropdown;
    private readonly DropDownMenu<SamplingMode> _samplingDropdown;

    public Collapsible Collapsible { get; }

    public IComponent Root => Collapsible;

    public event Action? OpenFileClicked;
    public event Action? OpenDirectoryClicked;
    public event Action? FullscreenClicked;
    public event Action? QuitClicked;
    public event Action<BackgroundMode>? BackgroundModeChanged;
    public event Action<SamplingMode>? SamplingModeChanged;

    public MenuSection(IPopupHost popupHost)
    {
        var openFileButton = new Button("OPEN")
        {
            CornerRadius = 0f,
            HorizontalSize = SizeMode.Expand
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

        _backgroundDropdown = new DropDownMenu<BackgroundMode>(
            popupHost,
            Enum.GetValues<BackgroundMode>(),
            m => m.Description(),
            SettingsManager.UiSettings.BackgroundMode,
            headerDisplay: m => $"BACKGROUND: {m.Description()}")
        {
            HorizontalSize = SizeMode.Expand,
            Padding = new Padding(0, 4f, 0, 0f)
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
            Padding = new Padding(0, 4f, 0, 12f)
        };
        quitRow.AddComponents(fullscreenButton, quitButton);

        var collapsible = new Collapsible("MENU")
        {
            HorizontalSize = SizeMode.Expand
        };
        collapsible.AddComponents(openRow, _backgroundDropdown, _samplingDropdown, quitRow);

        Collapsible = collapsible;
    }

    public void SetBackgroundMode(BackgroundMode mode) => _backgroundDropdown.Selected = mode;

    public void SetSamplingMode(SamplingMode mode) => _samplingDropdown.Selected = mode;

    public void Refresh(UIState state)
    {
        // Menu content is user-driven; nothing to refresh.
    }
}