namespace Lyra.PathUtils;

public static class PathUtils
{
    public static string? GetTopDirectory(List<string> paths)
    {
        if (paths.Count == 0)
            return null;

        var normalized = paths
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Select(NormalizeDirectory)
            .Distinct(PathComparer.CommonPathComparer)
            .ToList();

        if (normalized.Count == 0)
            return null;

        if (normalized.Count == 1)
            return normalized[0];

        var first = normalized[0];
        var commonLength = first.Length;

        foreach (var path in normalized.Skip(1))
        {
            var pairCommon = CommonLeadingLength(first, path);
            commonLength = Math.Min(commonLength, pairCommon);

            if (commonLength == 0)
                return null;
        }

        var common = first[..commonLength];

        // Directory boundary
        var lastSep = Math.Max(
            common.LastIndexOf(Path.DirectorySeparatorChar),
            common.LastIndexOf(Path.AltDirectorySeparatorChar));

        if (OperatingSystem.IsWindows())
        {
            var root = Path.GetPathRoot(common); // use common, not first
            if (!string.IsNullOrEmpty(root))
            {
                // If at/inside the root boundary, return root (covers drive + UNC roots)
                if (common.Length <= root.Length || lastSep < root.Length)
                    return root;
            }

            // Avoid returning bare "C:" if lastSep lands on "C:\"
            if (!string.IsNullOrEmpty(root) && root.Length >= 3 && lastSep == 2)
                return root;
        }
        else
        {
            if (lastSep == 0) // "/"
                return Path.DirectorySeparatorChar.ToString();
        }

        return lastSep <= 0 ? null : common[..lastSep];
    }

    public static string NormalizeDirectory(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return string.Empty;

        try
        {
            var full = Path.GetFullPath(path);

            if (File.Exists(full))
                full = Path.GetDirectoryName(full) ?? full;
            else if (Directory.Exists(full))
            {
                /* keep full */
            }
            else
            {
                // Fallback heuristic: if it has an extension, treat as file
                if (Path.HasExtension(full))
                    full = Path.GetDirectoryName(full) ?? full;
            }

            return Path.TrimEndingDirectorySeparator(full);
        }
        catch
        {
            return Path.TrimEndingDirectorySeparator(path);
        }
    }

    public static string GetCanonicalPath(string path, bool followSymlinks)
    {
        try
        {
            FileSystemInfo fsi =
                Directory.Exists(path) ? new DirectoryInfo(path) : new FileInfo(path);

            if (!followSymlinks)
                return fsi.FullName;

            var target = fsi.ResolveLinkTarget(true);
            return target?.FullName ?? fsi.FullName;
        }
        catch
        {
            return Path.GetFullPath(path);
        }
    }

    public static List<string> ValidateAndNormalizePaths(IEnumerable<string>? paths)
    {
        if (paths is null)
            return [];

        var distinct = new HashSet<string>(PathComparer.CommonPathComparer);
        var results = new List<string>();

        foreach (var raw in paths)
        {
            if (string.IsNullOrWhiteSpace(raw))
                continue;

            string full;
            try
            {
                full = Path.GetFullPath(raw);
            }
            catch
            {
                continue;
            }

            FileAttributes attr;
            try
            {
                attr = File.GetAttributes(full);
            }
            catch
            {
                continue; // missing / no access
            }

            var normalized = (attr & FileAttributes.Directory) != 0
                ? Path.TrimEndingDirectorySeparator(full)
                : full;

            if (distinct.Add(normalized))
                results.Add(normalized);
        }

        return results;
    }
    
    public static bool DirectoryHasSubdirectories(string dirPath)
    {
        ArgumentNullException.ThrowIfNull(dirPath);

        if (string.IsNullOrWhiteSpace(dirPath))
            throw new ArgumentException("Path cannot be empty or whitespace.", nameof(dirPath));
        
        if(!Directory.Exists(dirPath))
            throw new DirectoryNotFoundException(dirPath);
        
        try
        {
            return Directory.EnumerateDirectories(dirPath, "*", SearchOption.TopDirectoryOnly).Any();
        }
        catch
        {
            return true;
        }
    }

    public static bool HaveSameParentDirectory(IEnumerable<string> paths)
    {
        ArgumentNullException.ThrowIfNull(paths);

        string? baseDir = null;
        var any = false;

        foreach (var raw in paths)
        {
            if (string.IsNullOrWhiteSpace(raw))
                continue;

            any = true;
            var full = Path.GetFullPath(raw);

            string parentDir;
            bool isDirectory;

            // Determine directory membership with a single filesystem probe
            if (Directory.Exists(full))
            {
                parentDir = full;
                isDirectory = true;
            }
            else if (File.Exists(full))
            {
                parentDir = Path.GetDirectoryName(full) ?? throw new ArgumentException($"Cannot determine parent directory for file: {full}");
                isDirectory = false;
            }
            else
            {
                throw new ArgumentException($"Path does not exist (not validated): {full}");
            }

            if (baseDir is null)
            {
                baseDir = parentDir;
                continue;
            }

            if (!PathComparer.Equals(parentDir, baseDir))
                return false;

            // If the input is a directory, it must be exactly the baseDir (no subdir mixed in)
            if (isDirectory && !PathComparer.Equals(full, baseDir))
                return false;
        }

        return any ? true : throw new ArgumentException("Invalid paths in the collection. Validate first!");
    }

    public static bool IsSymlinkOrReparsePoint(string path)
    {
        try
        {
            // Windows: reparse points include junctions & symlinks
            if (OperatingSystem.IsWindows())
                return (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;

            FileSystemInfo fsi =
                Directory.Exists(path) ? new DirectoryInfo(path) : new FileInfo(path);

            return fsi.LinkTarget != null;
        }
        catch
        {
            return false;
        }
    }

    public static bool IsHidden(string path)
    {
        try
        {
            var trimmed = Path.TrimEndingDirectorySeparator(path);
            var name = Path.GetFileName(trimmed);

            if (string.IsNullOrEmpty(name))
                return false;

            if (name.StartsWith('.'))
                return true; // Unix-like "dotfile"

            if (OperatingSystem.IsWindows())
            {
                var attributes = File.GetAttributes(trimmed);
                return (attributes & FileAttributes.Hidden) != 0;
            }

            return false;
        }
        catch
        {
            return false;
        }
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