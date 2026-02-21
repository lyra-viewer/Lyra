using System.Reflection;
using System.Text;
using Lyra.Common.Settings.Enums;
using Lyra.Common.SystemExtensions;
using Tomlyn;
using static Lyra.Common.Settings.AppSettings;
using static Lyra.Common.Settings.UiSettings;

namespace Lyra.Common.Settings;

public static class SettingsManager
{
    private static readonly string AppSettingsFilepath = LyraIO.GetAppSettingsFile();
    private static readonly string UiSettingsFilepath = LyraIO.GetUiSettingsFile();

    private const int CurrentVersion = 1;

    private sealed class UserSettingsToml
    {
        public int Version { get; set; } = CurrentVersion;
        public int SamplingMode { get; set; } = (int)DefaultUiSettings.SamplingMode;
        public int BackgroundMode { get; set; } = (int)DefaultUiSettings.BackgroundMode;
        public int InfoLevel { get; set; } = (int)DefaultUiSettings.InfoLevel;
        public bool HelpBarVisible { get; set; } = DefaultUiSettings.HelpBarVisible;
    }

    private static string BuildUserSettingsToml(UiSettings s)
    {
        return $"""
                # Lyra Ui Settings (overwritten on exit)
                version = {CurrentVersion}

                sampling_mode = {(int)s.SamplingMode}
                background_mode = {(int)s.BackgroundMode}
                info_level = {(int)s.InfoLevel}
                help_bar_visible = {s.HelpBarVisible.ToString().ToLowerInvariant()}

                """;
    }

    private sealed class AppSettingsToml
    {
        public int Version { get; set; } = CurrentVersion;
        public string Renderer { get; set; } = DefaultAppSettings.Renderer.Alias();
        public string WindowStateOnStart { get; set; } = DefaultAppSettings.WindowStateOnStart.Alias();
        public string MidMouseButtonFunction { get; set; } = DefaultAppSettings.MidMouseButtonFunction.Alias();
        public bool PreserveUiSettings { get; set; } = DefaultAppSettings.PreserveUiSettings;
    }

    private static string BuildAppSettingsToml(AppSettings s)
    {
        return $"""
                # Lyra Application Settings
                version = {CurrentVersion}

                # Renderer used:
                # "opengl", "metal"
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

                """;
    }

    public static AppSettings LoadAppSettings()
    {
        var defaultTomlText = BuildAppSettingsToml(DefaultAppSettings);
        var dto = LoadOrReset<AppSettingsToml>(AppSettingsFilepath, defaultTomlText);

        if (dto.Version != CurrentVersion)
        {
            SaveAtomic(defaultTomlText, AppSettingsFilepath);
            return DefaultAppSettings;
        }

        var renderer = DefaultAppSettings.Renderer;
        if (TryParseByAlias(dto.Renderer, out Backend parsedBackend))
            renderer = parsedBackend;

        var windowState = DefaultAppSettings.WindowStateOnStart;
        if (TryParseByAlias(dto.WindowStateOnStart, out WindowState parsedWindowState))
            windowState = parsedWindowState;
        
        var midMouseButton = DefaultAppSettings.MidMouseButtonFunction;
        if (TryParseByAlias(dto.MidMouseButtonFunction, out MidMouseButtonFunction parsedMidMouseButton))
            midMouseButton = parsedMidMouseButton;

        return new AppSettings(renderer, windowState, midMouseButton, dto.PreserveUiSettings);
    }

    public static UiSettings LoadUiSettings()
    {
        var defaultTomlText = BuildUserSettingsToml(DefaultUiSettings);
        var dto = LoadOrReset<UserSettingsToml>(UiSettingsFilepath, defaultTomlText);

        if (dto.Version != CurrentVersion)
        {
            SaveAtomic(defaultTomlText, UiSettingsFilepath);
            return DefaultUiSettings;
        }

        var sampling = Enum.IsDefined(typeof(SamplingMode), dto.SamplingMode)
            ? (SamplingMode)dto.SamplingMode
            : DefaultUiSettings.SamplingMode;

        var background = Enum.IsDefined(typeof(BackgroundMode), dto.BackgroundMode)
            ? (BackgroundMode)dto.BackgroundMode
            : DefaultUiSettings.BackgroundMode;

        var info = Enum.IsDefined(typeof(InfoMode), dto.InfoLevel)
            ? (InfoMode)dto.InfoLevel
            : DefaultUiSettings.InfoLevel;

        return new UiSettings(sampling, background, info, dto.HelpBarVisible);
    }

    public static void SaveUiSettings(UiSettings uiSettings)
    {
        var uiSettingsToml = BuildUserSettingsToml(uiSettings);
        SaveAtomic(uiSettingsToml, UiSettingsFilepath);
    }

    private static void SaveAtomic(string toml, string filepath)
    {
        var dir = Path.GetDirectoryName(filepath);
        if (!string.IsNullOrWhiteSpace(dir))
            Directory.CreateDirectory(dir);

        var tmpPath = filepath + ".tmp";
        File.WriteAllText(tmpPath, toml.Replace("\r\n", "\n"), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        File.Move(tmpPath, filepath, overwrite: true);
    }

    private static T LoadOrReset<T>(string path, string defaultToml)
        where T : class, new()
    {
        try
        {
            if (!File.Exists(path))
            {
                SaveAtomic(defaultToml, path);
                return Toml.ToModel<T>(defaultToml);
            }

            var text = File.ReadAllText(path);

            // Syntax validation with diagnostics
            var doc = Toml.Parse(text);
            if (doc.HasErrors)
            {
                SaveAtomic(defaultToml, path);
                return Toml.ToModel<T>(defaultToml);
            }

            return Toml.ToModel<T>(text);
        }
        catch
        {
            SaveAtomic(defaultToml, path);
            return Toml.ToModel<T>(defaultToml);
        }
    }

    public static bool TryParseByAlias<TEnum>(string alias, out TEnum value)
        where TEnum : struct, Enum
    {
        value = default;

        if (string.IsNullOrWhiteSpace(alias))
            return false;

        var type = typeof(TEnum);
        foreach (var field in type.GetFields(BindingFlags.Public | BindingFlags.Static))
        {
            var aliasAttr = field.GetCustomAttribute<AliasAttribute>();
            if (aliasAttr != null && string.Equals(aliasAttr.Alias, alias, StringComparison.OrdinalIgnoreCase))
            {
                value = (TEnum)field.GetValue(null)!;
                return true;
            }
        }

        return false;
    }
}