using Lyra.Common;
using Lyra.FileLoader.Store;
using Lyra.Imaging.Detection;
using static Lyra.PathUtils.PathUtils;

namespace Lyra.FileLoader.Enumeration;

public static class FilePathProcessor
{
    /// <summary>
    /// How many files a scan opens to see whether a name that says nothing hides an image. Past it
    /// they are skipped, so a folder full of extensionless files cannot stall the scan.
    /// </summary>
    internal const int MaxIdentifiedPerScan = 2000;

    public static FileLoaderRecursion FileLoaderRecursion { get; set; } = FileLoaderRecursion.AsDesigned;
    public static bool IncludeHidden { get; set; } = false;
    public static bool FollowSymlinks { get; set; } = false;

    public static CollectionLoadResult ProcessImagePaths(
        List<string> paths,
        bool? recurseSubdirs,
        CancellationToken cancellationToken,
        Action? onFileEnumerated = null,
        Action? onSupportedFileDiscovered = null)
    {
        var dropContext = AnalyzeDrop(paths);
        if (dropContext.ExplicitPaths.Count == 0)
            return CollectionLoadResult.Empty(dropContext);

        // Resolve recursion policy. "AsDesigned" defaults to multi-dir recursion.
        var designedRecurse = dropContext.ExplicitDirectories.Count > 1 || (dropContext.ExplicitDirectories.Count == 1 && DirectoryHasSubdirectories(dropContext.ExplicitDirectories[0]));
        var recurse = ResolveRecursion(recurseSubdirs, designedRecurse);

        // Build enumeration plan from explicit input facts.
        var plan = BuildPlan(dropContext);

        // Enumerate and filter supported files. Each record carries its normalized
        // directory, captured here while it is already known (no recomputation downstream).
        var supported = CollectSupportedFiles(plan, recurse, out var allDirectories, cancellationToken, onFileEnumerated, onSupportedFileDiscovered);

        // UI / navigation metadata
        var uniqueDirs = GetUniqueDirectories(supported);
        var singleDirectory = uniqueDirs.Count == 1;
        var topDirectory = ComputeTopDirectory(dropContext.ExplicitPaths, hasFiles: supported.Count > 0, uniqueDirs);
        var collectionType = DecideCollectionType(dropContext);

        Logger.Info($"[FilePathProcessor] Collected {supported.Count} supported files. Recurse={recurse}, Input={dropContext.ExplicitPaths.Count}.");

        return new CollectionLoadResult(supported, allDirectories, singleDirectory, topDirectory, dropContext, collectionType);
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
            // The file itself stays explicit, so it opens even when its name is not an image's.
            if (!string.IsNullOrWhiteSpace(parent) && Directory.Exists(parent))
                return new EnumerationPlan([parent], [ctx.ExplicitFiles[0]]);

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

    private static List<FileRecord> CollectSupportedFiles(
        EnumerationPlan plan,
        bool recurseSubdirs,
        out List<string> allDirectories,
        CancellationToken cancellationToken,
        Action? onFileEnumerated,
        Action? onSupportedFileDiscovered)
    {
        // Full path -> its normalized directory, and what its bytes say when its name does not.
        // Dedup is by path; the directory is captured at discovery time.
        var all = new Dictionary<string, Found>(PathComparer.CommonPathComparer);
        var dirSet = new HashSet<string>(PathComparer.CommonPathComparer);
        var identified = 0;

        foreach (var f in plan.ExplicitFiles)
        {
            // Soft-cancel: keep what's already collected.
            if (cancellationToken.IsCancellationRequested)
                break;

            // Count as "processed" for UI purposes.
            onFileEnumerated?.Invoke();

            // Collect parent dir of explicit files.
            var parentRaw = Path.GetDirectoryName(f);
            var parentDir = string.IsNullOrWhiteSpace(parentRaw) ? string.Empty : NormalizeDirectory(parentRaw);
            if (parentDir.Length > 0)
                dirSet.Add(parentDir);

            // Chosen explicitly, so any name is worth a look inside.
            if (Recognise(f, parentDir, mayIdentify: true) is { } found && all.TryAdd(f, found))
                onSupportedFileDiscovered?.Invoke();
        }

        // Directory enumeration
        foreach (var dir in plan.RootDirectories)
        {
            // Soft-cancel: stop enumerating new directories, but keep collected results.
            if (cancellationToken.IsCancellationRequested)
                break;

            foreach (var (file, normalizedDir) in EnumerateFilesIterative(dir, recurseSubdirs, dirSet, cancellationToken))
            {
                onFileEnumerated?.Invoke();

                if (!all.ContainsKey(file) && Recognise(file, normalizedDir, mayIdentify: ImageFormat.MayBeMisnamed(Path.GetExtension(file))) is { } found && all.TryAdd(file, found))
                    onSupportedFileDiscovered?.Invoke();

                // Soft-cancel: stop after the current file.
                if (cancellationToken.IsCancellationRequested)
                    break;
            }

            if (cancellationToken.IsCancellationRequested)
                break;
        }

        allDirectories = dirSet.OrderBy(d => d, StringComparer.Ordinal).ToList();

        if (identified >= MaxIdentifiedPerScan)
            Logger.Warning($"[FilePathProcessor] Looked inside {MaxIdentifiedPerScan} files whose names say nothing; skipped the rest.");

        return all
            .Select(kv => new FileRecord(kv.Key, Path.GetFileName(kv.Key), kv.Value.Directory, Detected: kv.Value.Detected))
            .ToList();

        Found? Recognise(string fullPath, string directory, bool mayIdentify)
        {
            if (ImageFormat.IsSupported(Path.GetExtension(fullPath)))
                return new Found(directory, ImageFormatType.Unknown);

            if (!mayIdentify || identified >= MaxIdentifiedPerScan)
                return null;

            identified++;
            return FormatDetector.Identify(fullPath) is { } format ? new Found(directory, format) : null;
        }
    }

    /// <param name="Detected">What the file's bytes say it is, when its name does not.</param>
    private readonly record struct Found(string Directory, ImageFormatType Detected);

    private static List<string> GetUniqueDirectories(IReadOnlyList<FileRecord> supported)
    {
        return supported
            .Select(r => r.Directory)
            .Where(d => !string.IsNullOrWhiteSpace(d))
            .Distinct(PathComparer.CommonPathComparer)
            .ToList();
    }

    private static string? ComputeTopDirectory(IReadOnlyList<string> input, bool hasFiles, List<string> uniqueDirs)
    {
        if (hasFiles)
            return GetTopDirectory(uniqueDirs) ?? (uniqueDirs.Count == 1 ? uniqueDirs[0] : null);

        // No supported files found; fall back to dropped directories (for UI focus).
        var candidateDirs = input
            .Select(p => Directory.Exists(p) ? p : Path.GetDirectoryName(p))
            .Where(d => !string.IsNullOrWhiteSpace(d))
            .Select(d => NormalizeDirectory(d!))
            .ToList();

        return GetTopDirectory(candidateDirs);
    }

    // Hard cap on traversal depth. Protects against pathological filesystems
    // (deeply nested trees, symlink chains that the visited-set can't catch
    // because canonicalization fell back to the unresolved path).
    private const int MaxTraversalDepth = 256;

    private static IEnumerable<(string File, string Dir)> EnumerateFilesIterative(string root, bool recurseSubdirs, HashSet<string>? visitedDirectories, CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(root))
            yield break;

        var stack = new Stack<(string Dir, int Depth)>();
        var visited = new HashSet<string>(PathComparer.CommonPathComparer);
        var depthCappedLogged = false;
        stack.Push((root, 0));

        while (stack.Count > 0)
        {
            // Soft-cancel: stop traversal and return what have been yielded so far.
            if (cancellationToken.IsCancellationRequested)
                yield break;

            var (dir, depth) = stack.Pop();
            var canon = GetCanonicalPath(dir, FollowSymlinks);

            if (!visited.Add(canon))
                continue;

            // Track every visited directory for the full tree.
            var normalizedDir = NormalizeDirectory(dir);
            visitedDirectories?.Add(normalizedDir);

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
                    yield return (f, normalizedDir);
            }

            if (!recurseSubdirs)
                continue;

            if (depth >= MaxTraversalDepth)
            {
                if (!depthCappedLogged)
                {
                    Logger.Warning($"[FilePathProcessor] Max traversal depth ({MaxTraversalDepth}) reached at '{dir}'; descendants skipped.");
                    depthCappedLogged = true;
                }

                continue;
            }

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

                stack.Push((sub, depth + 1));
            }
        }
    }

    private static CollectionType DecideCollectionType(FileDropContext ctx)
    {
        // SingleDirectoryCollection ("open with" semantics)
        if (ctx.ExplicitFiles.Count == 1 && ctx.ExplicitDirectories.Count == 0 && ctx.IsSingleFileOpen)
            return CollectionType.SingleDirectoryCollection;

        // SingleDirectoryCollection (one directory if no subdirs, otherwise multi)
        if (ctx.ExplicitDirectories.Count == 1 && ctx.ExplicitFiles.Count == 0)
        {
            return DirectoryHasSubdirectories(ctx.ExplicitDirectories[0])
                ? CollectionType.MultiDirectorySelection
                : CollectionType.SingleDirectoryCollection;
        }

        // SingleDirectorySelection (many files, same directory)
        if (ctx.ExplicitFiles.Count > 1 && ctx.ExplicitDirectories.Count == 0 && ctx.IsSameDirectoryGroup)
            return CollectionType.SingleDirectorySelection;

        // MultiDirectorySelection
        return CollectionType.MultiDirectorySelection;
    }
}