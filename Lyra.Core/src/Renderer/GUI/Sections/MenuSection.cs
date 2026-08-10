using Lyra.Common.Settings;
using Lyra.Common.Settings.Enums;
using Lyra.Common.SystemExtensions;
using Lyra.UI;
using Lyra.UI.Components;
using Lyra.UI.Components.Controls;
using Lyra.UI.Components.Controls.Button;
using Lyra.UI.Components.Layout;

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
    public event Action? AboutClicked;
    public event Action<InitDisplayMode>? InitDisplayModeChanged;
    public event Action<BackgroundMode>? BackgroundModeChanged;
    public event Action<SamplingMode>? SamplingModeChanged;

    public MenuSection(IPopupHost popupHost)
    {
        _initDisplayModeDropdown = Dropdown(
            popupHost, "INIT DISPLAY MODE",
            SettingsManager.UiSettings.InitDisplayMode,
            mode => InitDisplayModeChanged?.Invoke(mode));

        _backgroundDropdown = Dropdown(
            popupHost, "BACKGROUND",
            SettingsManager.UiSettings.BackgroundMode,
            mode => BackgroundModeChanged?.Invoke(mode));

        _samplingDropdown = Dropdown(
            popupHost, "SAMPLING",
            SettingsManager.UiSettings.SamplingMode,
            mode => SamplingModeChanged?.Invoke(mode));

        Collapsible = new Collapsible("MENU")
            .ExpandH()
            .Children(
                Row(
                    MenuButton("OPEN").OnClick(() => OpenFileClicked?.Invoke()),
                    MenuButton("OPEN DIR").OnClick(() => OpenDirectoryClicked?.Invoke())),
                Row(
                    MenuButton("FULL SCREEN").OnClick(() => FullscreenClicked?.Invoke()),
                    MenuButton("QUIT", ButtonVariant.Danger).OnClick(() => QuitClicked?.Invoke())),
                Row(MenuButton("DUPLICATES FINDER").OnClick(() => ShowDuplicatesFinderClicked?.Invoke())),
                Row(MenuButton("ABOUT").OnClick(() => AboutClicked?.Invoke())),
                _initDisplayModeDropdown,
                _backgroundDropdown,
                _samplingDropdown,
                Separator());
    }

    private static HStack Row(params IComponent[] children) =>
        new HStack()
            .ExpandH()
            .Spacing(4f)
            .Transient()
            .PadTop(4f)
            .Children(children);

    private static Button MenuButton(string text, ButtonVariant variant = ButtonVariant.Default) =>
        new Button(text, variant)
            .CornerRadius(0f)
            .ExpandH();

    private static HStack Separator() =>
        new HStack()
            .ExpandH()
            .Transient()
            .PadBottom(12f);

    private static DropDownMenu<T> Dropdown<T>(
        IPopupHost popupHost,
        string label,
        T selected,
        Action<T> onChanged) where T : struct, Enum =>
        new DropDownMenu<T>(
                popupHost,
                Enum.GetValues<T>(),
                m => m.Description(),
                selected,
                headerDisplay: m => $"{label}: {m.Description()}")
            .ExpandH()
            .PadTop(4f)
            .OnSelectionChanged(onChanged);

    public void SetInitDisplayMode(InitDisplayMode mode) => _initDisplayModeDropdown.Selected = mode;

    public void SetBackgroundMode(BackgroundMode mode) => _backgroundDropdown.Selected = mode;

    public void SetSamplingMode(SamplingMode mode) => _samplingDropdown.Selected = mode;

    public void Refresh(UIState state)
    {
        // Menu content is user-driven; nothing to refresh.
    }
}