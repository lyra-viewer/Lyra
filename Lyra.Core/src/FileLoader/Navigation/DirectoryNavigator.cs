using Lyra.Common;
using Lyra.FileLoader.Enumeration;
using Lyra.FileLoader.Store;
using Lyra.PathUtils;

namespace Lyra.FileLoader.Navigation;

/// <summary>
/// Directory-aware view over <see cref="FileRecordDatabase"/>. Owns collection
/// semantics (collection type, top directory, the full directory tree, the
/// "x-in-directory" counter, directory-edge navigation) and delegates the raw
/// ordered list and cursor to the database.
/// </summary>
public static class DirectoryNavigator
{
    /// <summary>
    /// The default ordering for an enumerated collection: group by directory
    /// (so directory-edge navigation and the per-directory counter stay correct),
    /// then natural-sort by file name within each directory. This is Lyra's
    /// canonical enumeration order, relocated here from the file enumerator.
    /// </summary>
    public static readonly IComparer<FileRecord> DefaultComparer =
        Comparer<FileRecord>.Create(static (a, b) =>
        {
            var byDir = PathComparer.Compare(a.Directory, b.Directory);
            return byDir != 0 ? byDir : NaturalStringComparer.Instance.Compare(a.Name, b.Name);
        });

    private static CollectionType _collectionType = CollectionType.Undefined;
    private static bool? _singleDirectory;
    private static string? _topDirectory;

    /// All directories visited during file enumeration, including
    /// intermediate ones with no supported images. Used to build
    /// the complete directory tree in the UI.
    private static IReadOnlyList<string> _allDirectories = [];

    /// <summary>
    /// Incremented on every structural change (ApplyCollection, Purge). UIManager
    /// checks this to avoid rebuilding the directory tree every frame. A pure
    /// re-sort does not bump it: it reorders files but not directory membership.
    /// </summary>
    private static int _version;

    private static List<DirEntry>? _cachedFullTree;
    private static int _cachedFullTreeVersion = -1;

    public static int Version => _version;

    public static bool IsDuplicatesMode => FileRecordDatabase.InDuplicatesView;

    /// <summary>Enters duplicates mode (grouped records only). Returns false if nothing is grouped.</summary>
    public static bool EnterDuplicatesMode()
    {
        if (!FileRecordDatabase.EnterDuplicatesView())
            return false;

        _version++;
        return true;
    }

    public static void ExitDuplicatesMode()
    {
        if (!FileRecordDatabase.InDuplicatesView)
            return;

        FileRecordDatabase.ExitDuplicatesView();
        _version++;
    }

    public static void ApplyCollection(CollectionLoadResult result)
    {
        _topDirectory = result.TopDirectory;
        _allDirectories = result.AllDirectories;
        _singleDirectory = result.SingleDirectory;
        _collectionType = result.CollectionType;

        var anchor = ResolveAnchor(result.Files, result.DropContext.AnchorPath);
        FileRecordDatabase.Load(result.Files, DefaultComparer, anchor);
        _version++;
    }

    private static string? ResolveAnchor(IReadOnlyList<FileRecord> files, string? anchorCandidate)
    {
        // No explicit anchor (e.g. a directory drop): return null and let the
        // database default the cursor to index 0 of the *sorted* collection.
        // Don't fall back to files[0] here - files is still unsorted at this point.
        if (string.IsNullOrWhiteSpace(anchorCandidate))
            return null;

        foreach (var f in files)
        {
            if (PathComparer.Equals(f.Path, anchorCandidate))
                return f.Path;
        }

        return null;
    }

    public static string? GetCurrent() => FileRecordDatabase.Current?.Path;

    public static void MoveToNext() => FileRecordDatabase.MoveNext();
    public static void MoveToPrevious() => FileRecordDatabase.MovePrevious();
    public static void MoveToFirst() => FileRecordDatabase.MoveToFirst();
    public static void MoveToLast() => FileRecordDatabase.MoveToLast();

    public static void MoveToLeftEdge() => MoveToDirEdge(goToStart: true);
    public static void MoveToRightEdge() => MoveToDirEdge(goToStart: false);

    private static void MoveToDirEdge(bool goToStart)
    {
        var records = FileRecordDatabase.Records;
        var currentIndex = FileRecordDatabase.CurrentIndex;
        if ((uint)currentIndex >= (uint)records.Count)
            return;

        if (_singleDirectory is true)
        {
            if (goToStart) FileRecordDatabase.MoveToFirst();
            else FileRecordDatabase.MoveToLast();
            return;
        }

        var currentDir = records[currentIndex].Directory;
        if (string.IsNullOrEmpty(currentDir))
            return;

        var (start, end) = GetContiguousDirBounds(seedIndex: currentIndex, normalizedDir: currentDir);

        var edge = goToStart ? start : end;
        if (currentIndex != edge)
        {
            FileRecordDatabase.MoveToIndex(edge);
            return;
        }

        // Already at edge -> jump to neighbor directory's opposite edge
        var neighborProbe = goToStart ? start - 1 : end + 1;
        if ((uint)neighborProbe >= (uint)records.Count)
        {
            if (goToStart) FileRecordDatabase.MoveToFirst();
            else FileRecordDatabase.MoveToLast();
            return;
        }

        var neighborDir = records[neighborProbe].Directory;
        if (string.IsNullOrEmpty(neighborDir))
            return;

        var (ns, ne) = GetContiguousDirBounds(seedIndex: neighborProbe, normalizedDir: neighborDir);
        FileRecordDatabase.MoveToIndex(goToStart ? ne : ns);
    }

    public static void Purge(string filePath)
    {
        if (FileRecordDatabase.Remove(filePath))
            _version++;
    }

    public static bool HasNext() => FileRecordDatabase.Count > 0 && FileRecordDatabase.CurrentIndex < FileRecordDatabase.Count - 1;

    public static bool HasPrevious() => FileRecordDatabase.CurrentIndex > 0;

    public static bool IsFirst() => FileRecordDatabase.Count > 0 && FileRecordDatabase.CurrentIndex == 0;

    public static bool IsLast() => FileRecordDatabase.Count > 0 && FileRecordDatabase.CurrentIndex == FileRecordDatabase.Count - 1;

    /// <summary>
    /// Returns a slice of image file paths centered on the current image,
    /// including the current image and up to <paramref name="depth"/> images
    /// before and after it in the collection.
    /// </summary>
    public static string[] GetRange(int depth) => FileRecordDatabase.GetRange(depth);

    public static Navigation GetNavigation()
    {
        var records = FileRecordDatabase.Records;
        var currentIndex = FileRecordDatabase.CurrentIndex;

        var navigation = new Navigation
        {
            CollectionCount = records.Count,
            CollectionIndex = currentIndex + 1,
            DirectoryCount = null,
            DirectoryIndex = null
        };

        if (GetCollectionType() != CollectionType.MultiDirectorySelection || (uint)currentIndex >= (uint)records.Count)
            return navigation;

        var currentDir = records[currentIndex].Directory;
        if (string.IsNullOrEmpty(currentDir))
            return navigation;

        var (start, end) = GetContiguousDirBounds(currentIndex, currentDir);

        navigation.DirectoryCount = end - start + 1;
        navigation.DirectoryIndex = currentIndex - start + 1;
        return navigation;
    }

    private static (int Start, int End) GetContiguousDirBounds(int seedIndex, string normalizedDir)
    {
        var records = FileRecordDatabase.Records;

        var start = seedIndex;
        while (start > 0)
        {
            if (!PathComparer.Equals(records[start - 1].Directory, normalizedDir))
                break;

            start--;
        }

        var end = seedIndex;
        while (end < records.Count - 1)
        {
            if (!PathComparer.Equals(records[end + 1].Directory, normalizedDir))
                break;

            end++;
        }

        return (start, end);
    }

    public static string? GetTopDirectory() => _topDirectory;

    public static CollectionType GetCollectionType()
    {
        return FileRecordDatabase.Count == 0 ? CollectionType.Undefined : _collectionType;
    }

    /// <summary>
    /// Returns sorted unique normalized directory paths from the current records.
    /// </summary>
    public static List<string> GetDirectoryPaths()
    {
        var records = FileRecordDatabase.Records;
        if (records.Count == 0)
            return [];
        
        var dirs = new HashSet<string>(PathComparer.CommonPathComparer);
        for (var i = 0; i < records.Count; i++) // NO FOREACH!
        {
            var directory = records[i].Directory;
            if (!string.IsNullOrEmpty(directory))
                dirs.Add(directory);
        }

        var sorted = dirs.ToList();
        sorted.Sort(StringComparer.Ordinal);
        return sorted;
    }

    /// <summary>
    /// Returns all directories from the enumeration, including intermediate
    /// ones that contain no images. Each entry is marked with HasImages.
    /// Cached by version - returns the same list reference until the next
    /// structural change (ApplyCollection, Purge).
    /// </summary>
    public static List<DirEntry> GetFullDirectoryTree()
    {
        if (_cachedFullTreeVersion == _version && _cachedFullTree != null)
            return _cachedFullTree;

        if (_allDirectories.Count == 0)
        {
            _cachedFullTree = [];
            _cachedFullTreeVersion = _version;
            return _cachedFullTree;
        }

        var withImages = new HashSet<string>(GetDirectoryPaths(), PathComparer.CommonPathComparer);

        _cachedFullTree = _allDirectories
            .Select(d => new DirEntry(d, withImages.Contains(d)))
            .ToList();
        _cachedFullTreeVersion = _version;
        return _cachedFullTree;
    }

    /// <summary>
    /// Returns an immutable snapshot of the navigator's current state for
    /// the UI layer. Called every frame; the tree list is cached by version.
    /// </summary>
    public static DirectorySnapshot GetSnapshot() =>
        new(_version, _topDirectory, GetFullDirectoryTree(), GetCurrentDirectory());

    /// <summary>
    /// Returns the normalized directory of the current image.
    /// </summary>
    public static string? GetCurrentDirectory() => FileRecordDatabase.Current?.Directory;

    /// <summary>
    /// Moves to the first image in the given directory.
    /// Parameter must be a normalized absolute directory path
    /// (same format as returned by GetDirectoryPaths).
    /// Returns true if a matching image was found.
    /// </summary>
    public static bool MoveToFirstInDirectory(string normalizedDir)
    {
        var records = FileRecordDatabase.Records;
        for (var i = 0; i < records.Count; i++)
        {
            if (PathComparer.Equals(records[i].Directory, normalizedDir))
            {
                FileRecordDatabase.MoveToIndex(i);
                return true;
            }
        }

        return false;
    }

    public record struct Navigation
    {
        public int CollectionCount;
        public int CollectionIndex;
        public int? DirectoryIndex;
        public int? DirectoryCount;
    }
}