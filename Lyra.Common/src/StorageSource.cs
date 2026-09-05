using System.Collections.Concurrent;

namespace Lyra.Common;

/// <summary>
/// Which storage a file lives on, as a key that groups files that will read at the same speed.
/// </summary>
public static class StorageSource
{
    /// <summary>
    /// Resolving a mount means enumerating every mount on the machine, so it is cached per
    /// directory - navigation stays in one directory for long stretches, and mounts rarely move.
    /// </summary>
    private static readonly ConcurrentDictionary<string, string> Roots = new();
    
    public static string RootFor(string path)
    {
        if (string.IsNullOrEmpty(path))
            return string.Empty;

        string directory;
        try
        {
            var full = Path.GetFullPath(path);
            directory = Path.GetDirectoryName(full) ?? full;
        }
        catch (Exception ex)
        {
            Logger.Debug($"[StorageSource] Could not resolve '{path}': {ex.Message}");
            return path;
        }

        return Roots.GetOrAdd(directory, Resolve);
    }

    private static string Resolve(string directory)
    {
        try
        {
            var best = string.Empty;
            foreach (var drive in DriveInfo.GetDrives())
            {
                var name = drive.Name;
                if (name.Length > best.Length && directory.StartsWith(name, StringComparison.OrdinalIgnoreCase))
                    best = name;
            }

            if (best.Length > 0)
                return best;
        }
        catch (Exception ex)
        {
            Logger.Debug($"[StorageSource] Could not enumerate drives: {ex.Message}");
        }

        var root = Path.GetPathRoot(directory);
        return string.IsNullOrEmpty(root) ? directory : root;
    }
}