using Lyra.Common;

namespace Lyra.FileLoader;

public static class FilePathProcessor
{
    public static FileLoaderRecursion FileLoaderRecursion { get; set; } = FileLoaderRecursion.AsDesigned;
    public static bool IncludeHidden { get; set; } = false;
    public static bool FollowSymlinks { get; set; } = false;

    private static readonly StringComparer PathComparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    public static List<string> ProcessImagePaths(List<string> paths, bool? recurseSubdirs, out bool? singleDirectory, out string? topDirectory)
    {
        singleDirectory = null;
        topDirectory = null;

        paths = ValidateAndNormalizePaths(paths);
        if (paths.Count == 0)
            return [];

        var finalRecurse = FileLoaderRecursion switch
        {
            FileLoaderRecursion.Always => true,
            FileLoaderRecursion.Never => false,
            _ => recurseSubdirs ?? !IsSameDirectoryGroup(paths)
        };

        // Collect files from dropped files/dirs
        var allFiles = paths
            .SelectMany(path =>
            {
                if (File.Exists(path))
                    return [path];
                else if (Directory.Exists(path))
                    return EnumerateFilesIterative(path, finalRecurse);
                else
                    return [];
            })
            .ToHashSet(PathComparer);

        // Keep only supported
        var supported = allFiles
            .Where(file => ImageFormat.IsSupported(Path.GetExtension(file)))
            .Select(full => new
            {
                Full = full,
                Dir = Path.GetDirectoryName(full) ?? string.Empty,
                Name = Path.GetFileName(full)
            })
            .OrderBy(x => x.Dir, PathComparer)
            .ThenBy(x => x.Name, PathComparer)
            .Select(x => x.Full)
            .ToList();

        // Unique directories containing supported images
        var uniqueDirectories = supported
            .Select(Path.GetDirectoryName)
            .Where(d => d != null)
            .Distinct(PathComparer)
            .ToList();

        singleDirectory = uniqueDirectories.Count == 1;

        // Find topDirectory
        if (supported.Count == 0)
        {
            var candidateDirs = paths
                .Select(p => Directory.Exists(p) ? p : Path.GetDirectoryName(p)!)
                .Where(d => !string.IsNullOrWhiteSpace(d))
                .ToList();

            topDirectory = GetTopDirectory(candidateDirs) ?? string.Empty;
        }

        Logger.Info($"[FilePathProcessor] Collected {supported.Count} supported files from dropped paths.");

        return supported;
    }

    private static IEnumerable<string> EnumerateFilesIterative(string root, bool recurseSubdirs, CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(root))
            yield break;

        var stack = new Stack<string>();
        var visited = new HashSet<string>(PathComparer);
        stack.Push(root);

        while (stack.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var dir = stack.Pop();
            var canon = GetCanonicalPath(dir);

            if (!visited.Add(canon))
                continue; // already visited via symlink path

            // Files in current directory
            IEnumerable<string> filesHere;
            try
            {
                filesHere = Directory.EnumerateFiles(dir, "*", SearchOption.TopDirectoryOnly);
            }
            catch (Exception ex)
            {
                Logger.Warning($"[FilePathProcessor] Files in '{dir}' failed: {ex.Message}");
                continue;
            }

            foreach (var f in filesHere)
            {
                if (IncludeHidden || !IsHidden(f))
                    yield return f;
            }

            if (!recurseSubdirs)
                continue;

            // Subdirs (filtered)
            IEnumerable<string> subdirs;
            try
            {
                subdirs = Directory.EnumerateDirectories(dir, "*", SearchOption.TopDirectoryOnly);
            }
            catch (Exception ex)
            {
                Logger.Warning($"[FilePathProcessor] Dirs in '{dir}' failed: {ex.Message}");
                continue;
            }

            foreach (var sub in subdirs)
            {
                if (!IncludeHidden && IsHidden(sub))
                    continue;

                if (!FollowSymlinks && IsSymlinkOrReparsePoint(sub))
                    continue;

                stack.Push(sub);
            }
        }
    }

    private static bool IsSymlinkOrReparsePoint(string path)
    {
        try
        {
            // Windows: reparse points include junctions & symlinks
            if (OperatingSystem.IsWindows())
                return (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;

            var di = new DirectoryInfo(path);
            return di.LinkTarget != null;
        }
        catch
        {
            return false;
        }
    }

    private static bool IsHidden(string path)
    {
        try
        {
            var fileName = Path.GetFileName(path);
            if (string.IsNullOrEmpty(fileName))
                return false;

            if (fileName.StartsWith('.'))
                return true; // Unix-like "dotfile"

            if (OperatingSystem.IsWindows())
            {
                var attributes = File.GetAttributes(path);
                return (attributes & FileAttributes.Hidden) != 0;
            }

            return false;
        }
        catch
        {
            return true;
        }
    }

    private static string GetCanonicalPath(string dir)
    {
        try
        {
            var dirInfo = new DirectoryInfo(dir);

            if (!FollowSymlinks)
                return dirInfo.FullName;

            var target = dirInfo.ResolveLinkTarget(true);
            return target != null ? target.FullName : dirInfo.FullName;
        }
        catch
        {
            return Path.GetFullPath(dir);
        }
    }

    /// <summary>
    /// Validates and normalizes a collection of paths:
    /// <list type="bullet">
    /// <item><description>Converts relative paths to absolute using <see cref="Path.GetFullPath(string)"/>.</description></item>
    /// <item><description>Removes duplicates (case-insensitive on Windows, case-sensitive on Unix-like systems).</description></item>
    /// <item><description>Excludes non-existing files and directories.</description></item>
    /// <item><description>Trims trailing directory separators for directories.</description></item>
    /// </list>
    /// </summary>
    private static List<string> ValidateAndNormalizePaths(IEnumerable<string>? paths)
    {
        if (paths is null)
            return [];

        var distinct = new HashSet<string>(PathComparer);
        var results = new List<string>();

        foreach (var path in paths)
        {
            if (string.IsNullOrWhiteSpace(path))
                continue;

            string fullPath;
            try
            {
                fullPath = Path.GetFullPath(path);
            }
            catch
            {
                continue;
            }

            string canonical;
            if (Directory.Exists(fullPath))
            {
                canonical = Path.TrimEndingDirectorySeparator(fullPath);
            }
            else if (File.Exists(fullPath))
            {
                canonical = fullPath;
            }
            else
            {
                continue;
            }

            if (distinct.Add(canonical))
                results.Add(canonical);
        }

        return results;
    }

    /// <summary>
    /// Determines whether the provided paths belong to the same non-recursive directory group.
    /// </summary>
    /// <remarks>
    /// The group is valid if all items share the same top-level directory (no subdirectories mixed in).
    /// Mixing a directory with one of its subdirectories, or files from different directories, is invalid.
    /// </remarks>
    /// <param name="paths">A collection of validated and normalized file or directory paths.</param>
    /// <returns>
    /// <see langword="true"/> if all items share the same top-level directory and no subdirectories are included; 
    /// otherwise, <see langword="false"/>.
    /// </returns>
    /// <exception cref="ArgumentException">
    /// Thrown when the collection is null, empty, or contains invalid paths. 
    /// Call <see cref="ValidateAndNormalizePaths"/> first.
    /// </exception>
    private static bool IsSameDirectoryGroup(IEnumerable<string> paths)
    {
        if (paths is null)
            throw new ArgumentException("Path collection cannot be null.");

        var pathList = paths.Where(p => !string.IsNullOrWhiteSpace(p)).ToList();
        if (pathList.Count == 0)
            throw new ArgumentException("Invalid paths in the collection. Validate first!");

        // Case: single directory path
        if (pathList.Count == 1 && Directory.Exists(pathList[0]))
        {
            var dir = pathList[0];
            // Directory with subdirectories is not considered a single-directory group
            var hasSubdirs = Directory.EnumerateDirectories(dir, "*", SearchOption.TopDirectoryOnly).Any();
            return !hasSubdirs;
        }

        string? baseDir = null;

        // First pass — determine candidate base directory
        foreach (var raw in pathList)
        {
            var full = Path.GetFullPath(raw);

            string candidate;
            if (Directory.Exists(full))
            {
                candidate = full;
            }
            else if (File.Exists(full))
            {
                var parent = Path.GetDirectoryName(full) ?? throw new ArgumentException($"Cannot determine parent directory for file: {full}");
                candidate = parent;
            }
            else
            {
                throw new ArgumentException($"Path does not exist (not validated): {full}");
            }

            if (baseDir is null)
            {
                baseDir = candidate;
            }
            else if (!PathComparer.Equals(candidate, baseDir))
            {
                return false;
            }
        }

        // Second pass — enforce non-recursive rule
        foreach (var full in pathList.Select(Path.GetFullPath))
        {
            if (Directory.Exists(full))
            {
                if (!PathComparer.Equals(full, baseDir))
                    return false;
            }
            else
            {
                var parent = Path.GetDirectoryName(full)!;
                if (!PathComparer.Equals(parent, baseDir))
                    return false;
            }
        }

        return true;
    }

    private static string? GetTopDirectory(List<string> paths)
    {
        if (paths.Count == 0)
            return null;

        var normalized = paths.Distinct(PathComparer).ToList();
        if (normalized.Count == 1)
            return normalized[0];

        // Find common prefix
        var first = normalized[0];
        var commonLength = first.Length;

        foreach (var path in normalized.Skip(1))
        {
            commonLength = CommonLeadingLength(first, path);
            if (commonLength == 0)
                return null;
        }

        // Trim to directory boundary
        var common = first[..commonLength];
        var lastSep = common.LastIndexOf(Path.DirectorySeparatorChar);

        if (OperatingSystem.IsWindows())
        {
            // If the shared prefix is at or before the drive root, return the root (e.g., "C:\")
            var root = Path.GetPathRoot(first)!;
            if (lastSep <= root.Length - 1)
                return root;
        }

        if (!OperatingSystem.IsWindows())
        {
            if (lastSep == 0) // Root ("/...")
                return Path.DirectorySeparatorChar.ToString();
        }

        return lastSep <= 0 ? null : common[..lastSep];
    }

    private static int CommonLeadingLength(string a, string b)
    {
        var max = Math.Min(a.Length, b.Length);
        var ignoreCase = OperatingSystem.IsWindows();

        for (var i = 0; i < max; i++)
        {
            char ca = a[i], cb = b[i];
            if (ignoreCase)
            {
                // Crude but effective for filesystem semantics; avoids culture issues
                ca = char.ToUpperInvariant(ca);
                cb = char.ToUpperInvariant(cb);
            }

            if (ca != cb) return i;
        }

        return max;
    }
}