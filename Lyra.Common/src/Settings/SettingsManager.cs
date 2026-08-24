using Lyra.Common.Settings.Enums;
using Lyra.Common.SystemExtensions;
using static Lyra.Common.Settings.AppSettings;
using static Lyra.Common.Settings.UISettings;

namespace Lyra.Common.Settings;

public static class SettingsManager
{
    private static readonly string AppSettingsFilepath = LyraIO.GetAppSettingsFile();
    private static readonly string UiSettingsFilepath = LyraIO.GetUiSettingsFile();

    private const int CurrentVersion = 4;

    public const int MinExposureStops = -8;
    public const int MaxExposureStops = 8;

    public static AppSettings AppSettings = DefaultAppSettings;
    public static UISettings UiSettings = DefaultUiSettings;

    public static void LoadSettings()
    {
        LoadAppSettings();

        if (AppSettings.PreserveUiSettings)
            LoadUiSettings();
    }

    public static void SaveUiSettings(UISettings uiSettings)
    {
        if (!AppSettings.PreserveUiSettings)
            return;

        TomlSettingsFile.Save(BuildUserSettingsToml(uiSettings), UiSettingsFilepath);
    }

    private static void LoadAppSettings()
    {
        var defaultToml = BuildAppSettingsToml(DefaultAppSettings);
        var table = TomlSettingsFile.ReadOrCreate(AppSettingsFilepath, defaultToml);

        var version = table.GetInt("version", CurrentVersion);
        if (version != CurrentVersion)
        {
            Logger.Warning($"[SettingsManager] AppSettings version mismatch ({version} != {CurrentVersion}). Resetting.");
            TomlSettingsFile.Save(defaultToml, AppSettingsFilepath);
            return;
        }

        var rendererAlias = table.GetString("renderer", DefaultAppSettings.Renderer.Alias());
        var windowAlias = table.GetString("window_state_on_start", DefaultAppSettings.WindowStateOnStart.Alias());
        var midAlias = table.GetString("mid_mouse_button_function", DefaultAppSettings.MidMouseButtonFunction.Alias());
        var infoTextSize = Math.Clamp(table.GetInt("info_text_size", DefaultAppSettings.InfoTextSize), 4, 72);
        var helpTextSize = Math.Clamp(table.GetInt("help_text_size", DefaultAppSettings.HelpTextSize), 4, 72);

        var preserveUi = table.GetBool("preserve_ui_settings", DefaultAppSettings.PreserveUiSettings);
        var debug = table.GetBool("debug", DefaultAppSettings.Debug);
        var theme = table.GetString("theme", DefaultAppSettings.Theme);

        var renderer = DefaultAppSettings.Renderer;
        if (EnumExtensions.TryParseByAlias(rendererAlias, out Backend parsedBackend))
            renderer = parsedBackend;
        else
            Logger.Warning($"[SettingsManager] Unknown renderer '{rendererAlias}', using default '{renderer.Alias()}'");

        var windowState = DefaultAppSettings.WindowStateOnStart;
        if (EnumExtensions.TryParseByAlias(windowAlias, out WindowState parsedWindowState))
            windowState = parsedWindowState;
        else
            Logger.Warning($"[SettingsManager] Unknown window_state_on_start '{windowAlias}', using default '{windowState.Alias()}'");

        var midMouseButton = DefaultAppSettings.MidMouseButtonFunction;
        if (EnumExtensions.TryParseByAlias(midAlias, out MidMouseButtonFunction parsedMidMouseButton))
            midMouseButton = parsedMidMouseButton;
        else
            Logger.Warning($"[SettingsManager] Unknown mid_mouse_button_function '{midAlias}', using default '{midMouseButton.Alias()}'");

        AppSettings = new AppSettings(renderer, windowState, midMouseButton, infoTextSize, helpTextSize, preserveUi, debug, theme);
    }

    private static void LoadUiSettings()
    {
        var defaultToml = BuildUserSettingsToml(DefaultUiSettings);
        var table = TomlSettingsFile.ReadOrCreate(UiSettingsFilepath, defaultToml);

        var version = table.GetInt("version", CurrentVersion);
        if (version != CurrentVersion)
        {
            Logger.Warning($"[SettingsManager] UiSettings version mismatch ({version} != {CurrentVersion}). Resetting.");
            TomlSettingsFile.Save(defaultToml, UiSettingsFilepath);
            return;
        }

        var samplingRaw = table.GetInt("sampling_mode", (int)DefaultUiSettings.SamplingMode);
        var backgroundRaw = table.GetInt("background_mode", (int)DefaultUiSettings.BackgroundMode);
        var initDisplayRaw = table.GetInt("init_display_mode", (int)DefaultUiSettings.InitDisplayMode);
        var toneMapRaw = table.GetInt("tone_map_mode", (int)DefaultUiSettings.ToneMapMode);
        var exposure = Math.Clamp(table.GetInt("exposure_stops", DefaultUiSettings.ExposureStops), MinExposureStops, MaxExposureStops);
        var info = table.GetBool("info_visible", DefaultUiSettings.InfoVisible);
        var help = table.GetBool("help_visible", DefaultUiSettings.HelpVisible);
        var sidebar = table.GetBool("sidebar_visible", DefaultUiSettings.SidebarVisible);

        var sampling = Enum.IsDefined(typeof(SamplingMode), samplingRaw)
            ? (SamplingMode)samplingRaw
            : DefaultUiSettings.SamplingMode;

        if (!Enum.IsDefined(typeof(SamplingMode), samplingRaw))
            Logger.Warning($"[SettingsManager] Invalid sampling_mode={samplingRaw}, using default {(int)DefaultUiSettings.SamplingMode}");

        var background = Enum.IsDefined(typeof(BackgroundMode), backgroundRaw)
            ? (BackgroundMode)backgroundRaw
            : DefaultUiSettings.BackgroundMode;

        if (!Enum.IsDefined(typeof(BackgroundMode), backgroundRaw))
            Logger.Warning($"[SettingsManager] Invalid background_mode={backgroundRaw}, using default {(int)DefaultUiSettings.BackgroundMode}");

        var initDisplay = Enum.IsDefined(typeof(InitDisplayMode), initDisplayRaw)
            ? (InitDisplayMode)initDisplayRaw
            : DefaultUiSettings.InitDisplayMode;

        if (!Enum.IsDefined(typeof(InitDisplayMode), initDisplayRaw))
            Logger.Warning($"[SettingsManager] Invalid init_display_mode={initDisplayRaw}, using default {(int)DefaultUiSettings.InitDisplayMode}");

        var toneMap = Enum.IsDefined(typeof(ToneMapMode), toneMapRaw)
            ? (ToneMapMode)toneMapRaw
            : DefaultUiSettings.ToneMapMode;

        if (!Enum.IsDefined(typeof(ToneMapMode), toneMapRaw))
            Logger.Warning($"[SettingsManager] Invalid tone_map_mode={toneMapRaw}, using default {(int)DefaultUiSettings.ToneMapMode}");

        UiSettings = new UISettings(sampling, background, initDisplay, toneMap, exposure, info, help, sidebar);
    }

    private static string BuildUserSettingsToml(UISettings s)
    {
        return $"""
                # Lyra Ui Settings (overwritten on exit)
                version = {CurrentVersion}

                sampling_mode = {(int)s.SamplingMode}
                background_mode = {(int)s.BackgroundMode}
                init_display_mode = {(int)s.InitDisplayMode}
                tone_map_mode = {(int)s.ToneMapMode}
                exposure_stops = {s.ExposureStops}
                info_visible = {s.InfoVisible.ToString().ToLowerInvariant()}
                help_visible = {s.HelpVisible.ToString().ToLowerInvariant()}
                sidebar_visible = {s.SidebarVisible.ToString().ToLowerInvariant()}

                """;
    }

    private static string BuildAppSettingsToml(AppSettings s)
    {
        return $"""
                # Lyra Application Settings
                version = {CurrentVersion}

                # Renderer used:
                # "auto"   - chosen per platform and capability
                # "opengl" - portable, no EDR support
                # "metal"  - macOS only, required for extended dynamic range
                renderer = "{s.Renderer.Alias()}"

                # Window state on application start:
                # "maximized", "normal", "fullscreen"
                window_state_on_start = "{s.WindowStateOnStart.Alias()}"

                # Function of middle mouse button click:
                # "pan", "exit", "none"
                mid_mouse_button_function = "{s.MidMouseButtonFunction.Alias()}"

                # If true, restore last UI settings on start and save on exit.
                # If false, always use defaults.
                preserve_ui_settings = {s.PreserveUiSettings.ToString().ToLowerInvariant()}

                # UI text size:
                info_text_size = {s.InfoTextSize}
                help_text_size = {s.HelpTextSize}

                # Active theme (file name in the themes/ directory, without extension).
                # Leave blank to use the built-in default palette.
                theme = "{s.Theme}"

                # Debug mode:
                debug = {s.Debug.ToString().ToLowerInvariant()}

                """;
    }
}