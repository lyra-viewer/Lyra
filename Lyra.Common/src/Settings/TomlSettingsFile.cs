using System.Text;
using Tomlyn;
using Tomlyn.Model;
using Tomlyn.Parsing;

namespace Lyra.Common.Settings;

internal static class TomlSettingsFile
{
    public static TomlTable ReadOrCreate(string path, string defaultToml)
    {
        try
        {
            if (!File.Exists(path))
            {
                Logger.Warning($"[Settings] Missing settings file: {path}. Writing default.");
                Save(defaultToml, path);
                return TomlSerializer.Deserialize(defaultToml, LyraTomlContext.Default.TomlTable)!;
            }

            var text = File.ReadAllText(path);
            var doc = SyntaxParser.Parse(text);

            if (doc.HasErrors)
            {
                Logger.Error($"[Settings] TOML parse errors in: {path}. Resetting to default.");
                foreach (var d in doc.Diagnostics)
                    Logger.Error($"[Settings] TOML: {d}");

                BackupCorrupted(path, "parse diagnostics present");
                Save(defaultToml, path);
                return TomlSerializer.Deserialize(defaultToml, LyraTomlContext.Default.TomlTable)!;
            }

            var model = TomlSerializer.Deserialize(text, LyraTomlContext.Default.TomlTable)!;
            Logger.Debug($"[Settings] Parsed TOML table: {path}");
            return model;
        }
        catch (Exception ex)
        {
            Logger.Error($"[Settings] Failed to parse settings file: {path}. Resetting to default.");
            Logger.Error(ex.Message);

            BackupCorrupted(path, "hard parse failure", ex);
            Save(defaultToml, path);
            return TomlSerializer.Deserialize(defaultToml, LyraTomlContext.Default.TomlTable)!;
        }
    }

    /// <summary>Writes via a temp file + move so a crash mid-write can't truncate the live file.</summary>
    public static void Save(string toml, string path)
    {
        Logger.Info($"[Settings] Saving: {path}");

        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(dir))
            Directory.CreateDirectory(dir);

        var normalized = toml.Replace("\r\n", "\n");
        var tmpPath = path + ".tmp";

        try
        {
            File.WriteAllText(tmpPath, normalized, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            File.Move(tmpPath, path, overwrite: true);

            Logger.Debug($"[Settings] Saved OK: {path}");
        }
        catch (Exception ex)
        {
            Logger.Error($"[Settings] Save FAILED: {path}");
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

    private static void BackupCorrupted(string path, string reason, Exception? ex = null)
    {
        try
        {
            if (!File.Exists(path))
                return;

            var timestamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");
            var backup = $"{path}.corrupted.{timestamp}.toml";

            File.Move(path, backup, overwrite: true);

            Logger.Warning($"[Settings] Backed up corrupted settings file: {path} -> {backup}. Reason: {reason}");

            if (ex != null)
                Logger.Warning(ex.Message);
        }
        catch (Exception backupEx)
        {
            Logger.Error($"[Settings] Failed to back up corrupted file: {path}");
            Logger.Error(backupEx.Message);
        }
    }

    public static string GetString(this TomlTable table, string key, string fallback)
    {
        if (!table.TryGetValue(key, out var v) || v is null)
            return fallback;

        if (v is string s)
            return s;

        Logger.Warning($"[Settings] Key '{key}' expected string but was '{v.GetType().Name}'. Using fallback.");
        return fallback;
    }

    public static bool GetBool(this TomlTable table, string key, bool fallback)
    {
        if (!table.TryGetValue(key, out var v) || v is null)
            return fallback;

        if (v is bool b)
            return b;

        Logger.Warning($"[Settings] Key '{key}' expected bool but was '{v.GetType().Name}'. Using fallback.");
        return fallback;
    }

    public static int GetInt(this TomlTable table, string key, int fallback)
    {
        if (!table.TryGetValue(key, out var v) || v is null)
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
            Logger.Warning($"[Settings] Key '{key}' integer out of range. Using fallback.");
            return fallback;
        }
    }
}
