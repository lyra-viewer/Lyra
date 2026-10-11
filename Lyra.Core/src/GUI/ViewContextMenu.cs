using Lyra.Common.Settings;
using Lyra.Common.Settings.Enums;
using Lyra.Common.SystemExtensions;
using Lyra.UI;
using Lyra.UI.Components.Controls;
using SkiaSharp;

namespace Lyra.GUI;

/// <summary>The right-click menu over the image area.</summary>
public sealed class ViewContextMenu : IDisposable
{
    private readonly ContextMenu _menu;

    private readonly MenuItem _infoItem;
    private readonly MenuItem _sidebarItem;
    private readonly MenuItem _helpItem;

    private readonly List<Choice<SamplingMode>> _samplingItems;
    private readonly List<Choice<BackgroundMode>> _backgroundItems;
    private readonly List<Choice<InitDisplayMode>> _initDisplayModeItems;

    private readonly record struct Choice<T>(T Value, MenuItem Item);

    public event Action? InfoToggleClicked;
    public event Action? SidebarToggleClicked;
    public event Action? HelpToggleClicked;
    public event Action? ViewLockToggleClicked;
    public event Action<SamplingMode>? SamplingModeChanged;
    public event Action<BackgroundMode>? BackgroundModeChanged;
    public event Action<InitDisplayMode>? InitDisplayModeChanged;

    public ViewContextMenu(IPopupHost popupHost)
    {
        _menu = new ContextMenu(popupHost);

        _infoItem = _menu.AddItem("Info Section", () => InfoToggleClicked?.Invoke(), "[I]");
        _sidebarItem = _menu.AddItem("Sidebar", () => SidebarToggleClicked?.Invoke(), "[U]");
        _helpItem = _menu.AddItem("Help Overlay", () => HelpToggleClicked?.Invoke(), "[H]");

        _menu.AddSeparator();

        _samplingItems = Choices(
            _menu.AddSubmenu("Sampling", "[S]"),
            SettingsManager.UiSettings.SamplingMode,
            mode => SamplingModeChanged?.Invoke(mode)
        );

        _backgroundItems = Choices(
            _menu.AddSubmenu("Background", "[B]"),
            SettingsManager.UiSettings.BackgroundMode,
            mode => BackgroundModeChanged?.Invoke(mode)
        );

        var initDisplayMode = _menu.AddSubmenu("Initial Display Mode");
        var selectedInitMode = SettingsManager.UiSettings.InitDisplayMode;

        _initDisplayModeItems = Choices(
            initDisplayMode,
            selectedInitMode,
            mode => InitDisplayModeChanged?.Invoke(mode),
            skip: InitDisplayMode.Locked
        );

        initDisplayMode.AddSeparator();
        var lockItem = initDisplayMode.AddItem(InitDisplayMode.Locked.Description(), () => ViewLockToggleClicked?.Invoke(), "[L]");
        lockItem.IsChecked = selectedInitMode == InitDisplayMode.Locked;
        _initDisplayModeItems.Add(new Choice<InitDisplayMode>(InitDisplayMode.Locked, lockItem));
    }

    private static List<Choice<T>> Choices<T>(ContextSubmenu submenu, T selected, Action<T> onPicked, T? skip = null)
        where T : struct, Enum
    {
        var items = new List<Choice<T>>();

        foreach (var value in Enum.GetValues<T>())
        {
            if (EqualityComparer<T?>.Default.Equals(value, skip))
                continue;

            var item = submenu.AddItem(value.Description(), () => onPicked(value));
            item.IsChecked = EqualityComparer<T>.Default.Equals(value, selected);
            items.Add(new Choice<T>(value, item));
        }

        return items;
    }

    public void Open(SKPoint position) => _menu.Open(position);

    public void Refresh(UIState state)
    {
        _infoItem.IsChecked = state.AppStates.InfoVisible;
        _sidebarItem.IsChecked = state.AppStates.SidebarVisible;
        _helpItem.IsChecked = state.AppStates.HelpVisible;
    }

    public void SetSamplingMode(SamplingMode mode) => Check(_samplingItems, mode);

    public void SetBackgroundMode(BackgroundMode mode) => Check(_backgroundItems, mode);

    public void SetInitDisplayMode(InitDisplayMode mode) => Check(_initDisplayModeItems, mode);

    private static void Check<T>(List<Choice<T>> items, T selected) where T : struct, Enum
    {
        foreach (var (value, item) in items)
            item.IsChecked = EqualityComparer<T>.Default.Equals(value, selected);
    }

    public void Dispose() => _menu.Dispose();
}