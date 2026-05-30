using Tomlyn;
using Tomlyn.Model;

namespace Lyra.Common.Settings;

public static class ThemeLoader
{
    public static IReadOnlyList<string> ListThemes()
    {
        var dir = LyraIO.GetThemesDir();

        try
        {
            if (!Directory.Exists(dir))
                return [];

            return Directory.EnumerateFiles(dir, "*.toml")
                .Select(Path.GetFileNameWithoutExtension)
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Select(name => name!)
                .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        catch (Exception ex)
        {
            Logger.Error($"[ThemeLoader] Failed to list themes in: {dir}");
            Logger.Error(ex.Message);
            return [];
        }
    }

    public static Dictionary<string, Color32> LoadColors(string themeName)
    {
        var colorMap = new Dictionary<string, Color32>(StringComparer.OrdinalIgnoreCase);

        // Blank = "no theme"
        if (string.IsNullOrWhiteSpace(themeName))
            return colorMap;

        try
        {
            var path = LyraIO.GetThemeFile(themeName);

            if (!File.Exists(path))
            {
                Logger.Warning($"[ThemeLoader] Theme file not found: {path}");
                return colorMap;
            }

            var text = File.ReadAllText(path);
            var model = TomlSerializer.Deserialize(text, LyraTomlContext.Default.TomlTable)!;

            if (!model.TryGetValue("colors", out var value) || value is not TomlTable colors)
            {
                Logger.Warning($"[ThemeLoader] Theme '{themeName}' has no [colors] table.");
                return colorMap;
            }

            foreach (var (key, raw) in colors)
            {
                if (raw is not string hex)
                {
                    Logger.Warning($"[ThemeLoader] Color '{key}' in theme '{themeName}' is not a string. Skipping.");
                    continue;
                }

                if (Color32.TryParse(hex, out var color))
                    colorMap[key] = color;
                else
                    Logger.Warning($"[ThemeLoader] Color '{key}' in theme '{themeName}' has invalid hex '{hex}'. Skipping.");
            }

            Logger.Debug($"[ThemeLoader] Loaded {colorMap.Count} colors from theme '{themeName}'.");
        }
        catch (Exception ex)
        {
            Logger.Error($"[ThemeLoader] Failed to load theme '{themeName}'.");
            Logger.Error(ex.Message);
        }

        return colorMap;
    }
}