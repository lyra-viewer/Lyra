namespace Lyra.Common;

public static class LyraIO
{
    private const string BaseDir = "lyra-viewer";

    private const string UiSettingsFileName = "ui-settings.toml";
    private const string AppSettingsFileName = "app-settings.toml";
    private const string LogFileName = "log.txt";
    private const string LoadTimeDataFileName = "load-time-data.toml";

    private const string ThemesDirName = "themes";
    private const string ThemeExtension = ".toml";

    private static readonly Lazy<string> DataDir = new(GetDataDirectory);
    private static readonly Lazy<string> ConfigDir = new(GetConfigDirectory);
    private static readonly Lazy<string> ThemesDir = new(GetThemesDirectory);

    public static string GetUiSettingsFile() => Path.Combine(ConfigDir.Value, UiSettingsFileName);

    public static string GetAppSettingsFile() => Path.Combine(ConfigDir.Value, AppSettingsFileName);

    public static string GetThemesDir() => ThemesDir.Value;

    public static string GetThemeFile(string themeName) => Path.Combine(ThemesDir.Value, themeName + ThemeExtension);

    public static string GetLogFile() => Path.Combine(DataDir.Value, LogFileName);

    public static string GetLoadTimeFile() => Path.Combine(DataDir.Value, LoadTimeDataFileName);

    private static string GetDataDirectory()
    {
        var path = OperatingSystem.IsWindows()
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), BaseDir)
            : Path.Combine(GetXdgOrHomeFallback("XDG_DATA_HOME", Path.Combine(".local", "share")), BaseDir);

        Directory.CreateDirectory(path);
        return path;
    }

    private static string GetConfigDirectory()
    {
        var path = OperatingSystem.IsWindows()
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), BaseDir)
            : Path.Combine(GetXdgOrHomeFallback("XDG_CONFIG_HOME", ".config"), BaseDir);

        Directory.CreateDirectory(path);
        return path;
    }

    private static string GetThemesDirectory()
    {
        var path = Path.Combine(ConfigDir.Value, ThemesDirName);
        Directory.CreateDirectory(path);
        return path;
    }

    private static string GetXdgOrHomeFallback(string envVarName, string fallbackRelativeToHome)
    {
        var envVar = Environment.GetEnvironmentVariable(envVarName);

        if (!string.IsNullOrWhiteSpace(envVar))
            return Path.GetFullPath(envVar);

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Path.Combine(home, fallbackRelativeToHome);
    }
}