using Lyra.Common;
using Lyra.PathUtils;
using static Lyra.PathUtils.PathUtils;

namespace Lyra.FileLoader;

public static class FilePathProcessor
{
    public static FileLoaderRecursion FileLoaderRecursion { get; set; } = FileLoaderRecursion.AsDesigned;
    public static bool IncludeHidden { get; set; } = false;
    public static bool FollowSymlinks { get; set; } = false;
    
    public static List<string> ProcessImagePaths(
        List<string> paths,
        bool? recurseSubdirs,
        out bool? singleDirectory,
        out string? topDirectory,
        out FileDropContext dropContext,
        CancellationToken cancellationToken,
        Action? onFileEnumerated = null,
        Action? onSupportedFileDiscovered = null)
    {
        singleDirectory = null;
        topDirectory = null;

        dropContext = AnalyzeDrop(paths);
        if (dropContext.ExplicitPaths.Count == 0)
            return [];

        // Resolve recursion policy. "AsDesigned" defaults to multi-dir recursion.
        var designedRecurse = dropContext.ExplicitDirectories.Count > 1 || (dropContext.ExplicitDirectories.Count == 1 && DirectoryHasSubdirectories(dropContext.ExplicitDirectories[0]));
        var recurse = ResolveRecursion(recurseSubdirs, designedRecurse);

        // Build enumeration plan from explicit input facts.
        var plan = BuildPlan(dropContext);

        // Enumerate, filter supported, and sort.
        var supported = CollectSupportedFiles(plan, recurse, cancellationToken, onFileEnumerated, onSupportedFileDiscovered);

        // UI metadata
        var uniqueDirs = GetUniqueDirectories(supported);
        singleDirectory = uniqueDirs.Count == 1;
        topDirectory = ComputeTopDirectory(dropContext.ExplicitPaths, supported, uniqueDirs);

        Logger.Info($"[FilePathProcessor] Collected {supported.Count} supported files. Recurse={recurse}, Input={dropContext.ExplicitPaths.Count}.");

        return supported;
    }

    private static FileDropContext AnalyzeDrop(IEnumerable<string>? paths)
    {
        var explicitPaths = ValidateAndNormalizePaths(paths);
        if (explicitPaths.Count == 0)
        {
            return new FileDropContext(
                ExplicitPaths: [],
                ExplicitFiles: [],
                ExplicitDirectories: [],
                IsSameDirectoryGroup: false,
                AnchorPath: null,
                IsSingleFileOpen: false);
        }

        var explicitFiles = new List<string>();
        var explicitDirs = new List<string>();

        foreach (var p in explicitPaths)
        {
            if (File.Exists(p))
                explicitFiles.Add(p);
            else if (Directory.Exists(p))
                explicitDirs.Add(p);
        }

        // "Open with" semantics: exactly one explicit path, and it's a file.
        var isSingleFileOpen = explicitPaths.Count == 1 && explicitFiles.Count == 1 && explicitDirs.Count == 0;

        var isSameDirGroup = false;
        if (explicitDirs.Count == 0 && explicitFiles.Count > 0)
        {
            isSameDirGroup = HaveSameParentDirectory(explicitFiles);
        }

        // Anchor: for single-file open, it's that file; otherwise first explicit file if any.
        string? anchor = null;
        if (isSingleFileOpen || explicitFiles.Count > 0)
            anchor = explicitFiles[0];

        return new FileDropContext(
            ExplicitPaths: explicitPaths,
            ExplicitFiles: explicitFiles,
            ExplicitDirectories: explicitDirs,
            IsSameDirectoryGroup: isSameDirGroup,
            AnchorPath: anchor,
            IsSingleFileOpen: isSingleFileOpen);
    }

    private static bool ResolveRecursion(bool? recurseSubdirs, bool designedRecurse)
    {
        return FileLoaderRecursion switch
        {
            FileLoaderRecursion.Always => true,
            FileLoaderRecursion.Never => false,
            _ => recurseSubdirs ?? designedRecurse
        };
    }

    private sealed record EnumerationPlan(List<string> RootDirectories, List<string> ExplicitFiles);

    private static EnumerationPlan BuildPlan(FileDropContext ctx)
    {
        // Single-file "open with": scan the parent directory (non-recursive by default).
        if (ctx is { IsSingleFileOpen: true, ExplicitFiles.Count: 1 })
        {
            var parent = Path.GetDirectoryName(ctx.ExplicitFiles[0]);
            if (!string.IsNullOrWhiteSpace(parent) && Directory.Exists(parent))
                return new EnumerationPlan([parent], []);

            // Fallback: keep the explicit file.
            return new EnumerationPlan([], [ctx.ExplicitFiles[0]]);
        }

        // Many explicit files in the same directory: selection only (do not expand to siblings).
        if (ctx.ExplicitDirectories.Count == 0 && ctx.ExplicitFiles.Count > 1 && ctx.IsSameDirectoryGroup)
        {
            return new EnumerationPlan([], ctx.ExplicitFiles.ToList());
        }

        // Directory drops and/or multi-directory selection: enumerate directories, include explicit files.
        return new EnumerationPlan(ctx.ExplicitDirectories.ToList(), ctx.ExplicitFiles.ToList());
    }

    private static List<string> CollectSupportedFiles(
        EnumerationPlan plan,
        bool recurseSubdirs,
        CancellationToken cancellationToken,
        Action? onFileEnumerated,
        Action? onSupportedFileDiscovered)
    {
        var all = new HashSet<string>(PathComparer.CommonPathComparer);

        foreach (var f in plan.ExplicitFiles)
        {
            // Soft-cancel: keep what's already collected.
            if (cancellationToken.IsCancellationRequested)
                break;

            // Count as "processed" for UI purposes.
            onFileEnumerated?.Invoke();

            // Fast-filter before filling the set; the final pipeline still sorts.
            if (IsSupportedFile(f) && all.Add(f))
                onSupportedFileDiscovered?.Invoke();
        }

        // Directory enumeration
        foreach (var dir in plan.RootDirectories)
        {
            // Soft-cancel: stop enumerating new directories, but keep collected results.
            if (cancellationToken.IsCancellationRequested)
                break;

            foreach (var f in EnumerateFilesIterative(dir, recurseSubdirs, cancellationToken))
            {
                onFileEnumerated?.Invoke();

                if (IsSupportedFile(f) && all.Add(f))
                    onSupportedFileDiscovered?.Invoke();

                // Soft-cancel: stop after the current file.
                if (cancellationToken.IsCancellationRequested)
                    break;
            }

            if (cancellationToken.IsCancellationRequested)
                break;
        }

        // Keep supported + stable ordering
        return all
            .Select(full => new
            {
                Full = full,
                Dir = NormalizeDirectory(Path.GetDirectoryName(full) ?? string.Empty),
                Name = Path.GetFileName(full)
            })
            .OrderBy(x => x.Dir, PathComparer.CommonPathComparer)
            .ThenBy(x => x.Name, PathComparer.CommonPathComparer)
            .Select(x => x.Full)
            .ToList();

        static bool IsSupportedFile(string fullPath) => ImageFormat.IsSupported(Path.GetExtension(fullPath));
    }

    private static List<string> GetUniqueDirectories(List<string> supported)
    {
        return supported
            .Select(Path.GetDirectoryName)
            .Where(d => !string.IsNullOrWhiteSpace(d))
            .Select(d => NormalizeDirectory(d!))
            .Distinct(PathComparer.CommonPathComparer)
            .ToList();
    }

    private static string? ComputeTopDirectory(IReadOnlyList<string> input, List<string> supported, List<string> uniqueDirs)
    {
        if (supported.Count > 0)
            return GetTopDirectory(uniqueDirs) ?? (uniqueDirs.Count == 1 ? uniqueDirs[0] : null);

        // No supported files found; fall back to dropped directories (for UI focus).
        var candidateDirs = input
            .Select(p => Directory.Exists(p) ? p : Path.GetDirectoryName(p))
            .Where(d => !string.IsNullOrWhiteSpace(d))
            .Select(d => NormalizeDirectory(d!))
            .ToList();

        return GetTopDirectory(candidateDirs);
    }

    private static IEnumerable<string> EnumerateFilesIterative(string root, bool recurseSubdirs, CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(root))
            yield break;

        var stack = new Stack<string>();
        var visited = new HashSet<string>(PathComparer.CommonPathComparer);
        stack.Push(root);

        while (stack.Count > 0)
        {
            // Soft-cancel: stop traversal and return what have been yielded so far.
            if (cancellationToken.IsCancellationRequested)
                yield break;

            var dir = stack.Pop();
            var canon = GetCanonicalPath(dir, FollowSymlinks);

            if (!visited.Add(canon))
                continue;

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
}