using System.Reflection;
using System.Text;
using Lyra.Common.Settings.Enums;
using Lyra.Common.SystemExtensions;
using Tomlyn;
using Tomlyn.Model;
using Tomlyn.Parsing;
using static Lyra.Common.Settings.AppSettings;
using static Lyra.Common.Settings.UiSettings;

namespace Lyra.Common.Settings;

public static class SettingsManager
{
    private static readonly string AppSettingsFilepath = LyraIO.GetAppSettingsFile();
    private static readonly string UiSettingsFilepath = LyraIO.GetUiSettingsFile();

    private const int CurrentVersion = 2;

    public static AppSettings AppSettings = DefaultAppSettings;
    public static UiSettings UiSettings = DefaultUiSettings;

    public static void LoadSettings()
    {
        LoadAppSettings();

        if (AppSettings.PreserveUiSettings)
            LoadUiSettings();
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

                # UI text size:
                info_text_size = {s.InfoTextSize}
                help_text_size = {s.HelpTextSize}
                
                """;
    }

    private static void LoadAppSettings()
    {
        var defaultToml = BuildAppSettingsToml(DefaultAppSettings);
        var table = ParseTableOrReset(AppSettingsFilepath, defaultToml);

        var version = GetInt(table, "version", CurrentVersion);
        if (version != CurrentVersion)
        {
            Logger.Warning($"[SettingsManager] AppSettings version mismatch ({version} != {CurrentVersion}). Resetting.");
            SaveAtomic(defaultToml, AppSettingsFilepath);
            return;
        }

        var rendererAlias = GetString(table, "renderer", DefaultAppSettings.Renderer.Alias());
        var windowAlias = GetString(table, "window_state_on_start", DefaultAppSettings.WindowStateOnStart.Alias());
        var midAlias = GetString(table, "mid_mouse_button_function", DefaultAppSettings.MidMouseButtonFunction.Alias());
        var infoTextSize = Math.Clamp(GetInt(table, "info_text_size", DefaultAppSettings.InfoTextSize), 4, 72);
        var helpTextSize = Math.Clamp(GetInt(table, "help_text_size", DefaultAppSettings.HelpTextSize), 4, 72);
        
        var preserveUi = GetBool(table, "preserve_ui_settings", DefaultAppSettings.PreserveUiSettings);
        
        var renderer = DefaultAppSettings.Renderer;
        if (TryParseByAlias(rendererAlias, out Backend parsedBackend))
            renderer = parsedBackend;
        else
            Logger.Warning($"[SettingsManager] Unknown renderer '{rendererAlias}', using default '{renderer.Alias()}'");

        var windowState = DefaultAppSettings.WindowStateOnStart;
        if (TryParseByAlias(windowAlias, out WindowState parsedWindowState))
            windowState = parsedWindowState;
        else
            Logger.Warning($"[SettingsManager] Unknown window_state_on_start '{windowAlias}', using default '{windowState.Alias()}'");

        var midMouseButton = DefaultAppSettings.MidMouseButtonFunction;
        if (TryParseByAlias(midAlias, out MidMouseButtonFunction parsedMidMouseButton))
            midMouseButton = parsedMidMouseButton;
        else
            Logger.Warning($"[SettingsManager] Unknown mid_mouse_button_function '{midAlias}', using default '{midMouseButton.Alias()}'");

        AppSettings = new AppSettings(renderer, windowState, midMouseButton, infoTextSize, helpTextSize, preserveUi);
    }

    private static void LoadUiSettings()
    {
        var defaultToml = BuildUserSettingsToml(DefaultUiSettings);
        var table = ParseTableOrReset(UiSettingsFilepath, defaultToml);

        var version = GetInt(table, "version", CurrentVersion);
        if (version != CurrentVersion)
        {
            Logger.Warning($"[SettingsManager] UiSettings version mismatch ({version} != {CurrentVersion}). Resetting.");
            SaveAtomic(defaultToml, UiSettingsFilepath);
            return;
        }

        var samplingRaw = GetInt(table, "sampling_mode", (int)DefaultUiSettings.SamplingMode);
        var backgroundRaw = GetInt(table, "background_mode", (int)DefaultUiSettings.BackgroundMode);
        var infoRaw = GetInt(table, "info_level", (int)DefaultUiSettings.InfoLevel);
        var help = GetBool(table, "help_bar_visible", DefaultUiSettings.HelpBarVisible);

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

        var info = Enum.IsDefined(typeof(InfoMode), infoRaw)
            ? (InfoMode)infoRaw
            : DefaultUiSettings.InfoLevel;

        if (!Enum.IsDefined(typeof(InfoMode), infoRaw))
            Logger.Warning($"[SettingsManager] Invalid info_level={infoRaw}, using default {(int)DefaultUiSettings.InfoLevel}");

        UiSettings = new UiSettings(sampling, background, info, help);
    }

    public static void SaveUiSettings(UiSettings uiSettings)
    {
        if (!AppSettings.PreserveUiSettings)
            return;

        var uiToml = BuildUserSettingsToml(uiSettings);
        SaveAtomic(uiToml, UiSettingsFilepath);
    }

    private static TomlTable ParseTableOrReset(string path, string defaultToml)
    {
        try
        {
            if (!File.Exists(path))
            {
                Logger.Warning($"[SettingsManager] Missing settings file: {path}. Writing default.");
                SaveAtomic(defaultToml, path);
                return TomlSerializer.Deserialize(defaultToml, LyraTomlContext.Default.TomlTable)!;
            }

            var text = File.ReadAllText(path);
            var doc = SyntaxParser.Parse(text);

            if (doc.HasErrors)
            {
                Logger.Error($"[SettingsManager] TOML parse errors in: {path}. Resetting to default.");
                foreach (var d in doc.Diagnostics)
                    Logger.Error($"[SettingsManager] TOML: {d}");

                BackupCorruptedFile(path, "TomlSerializer.Deserialize diagnostics present");
                SaveAtomic(defaultToml, path);
                return TomlSerializer.Deserialize(defaultToml, LyraTomlContext.Default.TomlTable)!;
            }

            var model = TomlSerializer.Deserialize(text, LyraTomlContext.Default.TomlTable)!;
            Logger.Debug($"[SettingsManager] Parsed TOML table: {path}");
            return model;
        }
        catch (Exception ex)
        {
            Logger.Error($"[SettingsManager] Failed to parse settings file: {path}. Resetting to default.");
            Logger.Error(ex.Message);

            BackupCorruptedFile(path, "ParseTableOrReset hard failure", ex);

            SaveAtomic(defaultToml, path);
            return TomlSerializer.Deserialize(defaultToml, LyraTomlContext.Default.TomlTable)!;
        }
    }

    private static string GetString(TomlTable t, string key, string fallback)
    {
        if (!t.TryGetValue(key, out var v) || v is null)
            return fallback;

        if (v is string s)
            return s;

        Logger.Warning($"[SettingsManager] Key '{key}' expected string but was '{v.GetType().Name}'. Using fallback.");
        return fallback;
    }

    private static bool GetBool(TomlTable t, string key, bool fallback)
    {
        if (!t.TryGetValue(key, out var v) || v is null)
            return fallback;

        if (v is bool b)
            return b;

        Logger.Warning($"[SettingsManager] Key '{key}' expected bool but was '{v.GetType().Name}'. Using fallback.");
        return fallback;
    }

    private static int GetInt(TomlTable t, string key, int fallback)
    {
        if (!t.TryGetValue(key, out var v) || v is null)
            return fallback;

        try
        {
            return v switch
            {
                int i => i,
                long l => checked((int)l), // Tomlyn often uses long for integers
                _ => fallback
            };
        }
        catch (Exception)
        {
            Logger.Warning($"[SettingsManager] Key '{key}' integer out of range. Using fallback.");
            return fallback;
        }
    }

    private static void SaveAtomic(string toml, string filepath)
    {
        Logger.Info($"[SettingsManager] Saving: {filepath}");

        var dir = Path.GetDirectoryName(filepath);
        if (!string.IsNullOrWhiteSpace(dir))
            Directory.CreateDirectory(dir);

        var normalized = toml.Replace("\r\n", "\n");
        var tmpPath = filepath + ".tmp";

        try
        {
            File.WriteAllText(tmpPath, normalized, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            File.Move(tmpPath, filepath, overwrite: true);

            Logger.Debug($"[SettingsManager] Saved OK: {filepath}");
        }
        catch (Exception ex)
        {
            Logger.Error($"[SettingsManager] Save FAILED: {filepath}");
            Logger.Error(ex.Message);

            try
            {
                if (File.Exists(tmpPath)) File.Delete(tmpPath);
            }
            catch
            {
                /* ignore */
            }

            throw;
        }
    }

    private static void BackupCorruptedFile(string path, string reason, Exception? ex = null)
    {
        try
        {
            if (!File.Exists(path))
                return;

            var timestamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");
            var backup = $"{path}.corrupted.{timestamp}.toml";

            File.Move(path, backup, overwrite: true);

            Logger.Warning($"[SettingsManager] Backed up corrupted settings file: {path} -> {backup}. Reason: {reason}");

            if (ex != null)
                Logger.Warning(ex.Message);
        }
        catch (Exception backupEx)
        {
            Logger.Error($"[SettingsManager] Failed to back up corrupted file: {path}");
            Logger.Error(backupEx.Message);
        }
    }

    private static bool TryParseByAlias<TEnum>(string alias, out TEnum value)
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