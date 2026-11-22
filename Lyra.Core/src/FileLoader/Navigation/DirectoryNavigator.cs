using Lyra.Common;
using Lyra.FileLoader.Enumeration;
using Lyra.FileLoader.Store;
using Lyra.PathUtils;

namespace Lyra.FileLoader.Navigation;

/// <summary>
/// Directory-aware view over <see cref="FileRecordDatabase"/>. Owns collection
/// semantics (collection type, top directory, the full directory tree, the
/// "x-in-directory" counter, directory-edge navigation) and delegates the raw
/// ordered list and cursor to the database. Not thread-safe: main thread only.
/// </summary>
public sealed class DirectoryNavigator
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

    private readonly FileRecordDatabase _store = new();
    public FileRecordDatabase Store => _store;

    private CollectionType _collectionType = CollectionType.Undefined;
    private bool? _singleDirectory;
    private string? _topDirectory;

    /// All directories visited during file enumeration, including
    /// intermediate ones with no supported images. Used to build
    /// the complete directory tree in the UI.
    private IReadOnlyList<string> _allDirectories = [];

    /// <summary>
    /// Incremented on every structural change (ApplyCollection, Purge). Keys the
    /// cached tree here, and travels in <see cref="GetSnapshot"/> so the directory
    /// tree presenter rebuilds only when it changes. A pure re-sort does not bump
    /// it: it reorders files but not directory membership.
    /// </summary>
    private int _version;

    private List<DirEntry>? _cachedFullTree;
    private int _cachedFullTreeVersion = -1;

    public bool HasDuplicatesSubset => _store.InDuplicatesView;

    /// <summary>Narrows the records to the grouped ones. Returns false if nothing is grouped.</summary>
    public bool ApplyDuplicatesSubset()
    {
        if (!_store.EnterDuplicatesView())
            return false;

        _version++;
        return true;
    }

    /// <summary>Restores the full collection if the duplicates subset is applied.</summary>
    public void RestoreFullCollection()
    {
        if (!_store.InDuplicatesView)
            return;

        _store.ExitDuplicatesView();
        _version++;
    }

    public void ApplyCollection(CollectionLoadResult result)
    {
        _topDirectory = result.TopDirectory;
        _allDirectories = result.AllDirectories;
        _singleDirectory = result.SingleDirectory;
        _collectionType = result.CollectionType;

        var anchor = ResolveAnchor(result.Files, result.DropContext.AnchorPath);
        _store.Load(result.Files, DefaultComparer, anchor);
        _version++;
    }

    private string? ResolveAnchor(IReadOnlyList<FileRecord> files, string? anchorCandidate)
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

    public string? GetCurrent() => _store.Current?.Path;

    public void MoveToNext() => _store.MoveNext();
    public void MoveToPrevious() => _store.MovePrevious();
    public void MoveToFirst() => _store.MoveToFirst();
    public void MoveToLast() => _store.MoveToLast();

    public void MoveToLeftEdge() => MoveToDirEdge(goToStart: true);
    public void MoveToRightEdge() => MoveToDirEdge(goToStart: false);

    private void MoveToDirEdge(bool goToStart)
    {
        var records = _store.Records;
        var currentIndex = _store.CurrentIndex;
        if ((uint)currentIndex >= (uint)records.Count)
            return;

        if (_singleDirectory is true)
        {
            if (goToStart) _store.MoveToFirst();
            else _store.MoveToLast();
            return;
        }

        var currentDir = records[currentIndex].Directory;
        if (string.IsNullOrEmpty(currentDir))
            return;

        var (start, end) = GetContiguousDirBounds(seedIndex: currentIndex, normalizedDir: currentDir);

        var edge = goToStart ? start : end;
        if (currentIndex != edge)
        {
            _store.MoveToIndex(edge);
            return;
        }

        // Already at edge -> jump to neighbor directory's opposite edge
        var neighborProbe = goToStart ? start - 1 : end + 1;
        if ((uint)neighborProbe >= (uint)records.Count)
        {
            if (goToStart) _store.MoveToFirst();
            else _store.MoveToLast();
            return;
        }

        var neighborDir = records[neighborProbe].Directory;
        if (string.IsNullOrEmpty(neighborDir))
            return;

        var (ns, ne) = GetContiguousDirBounds(seedIndex: neighborProbe, normalizedDir: neighborDir);
        _store.MoveToIndex(goToStart ? ne : ns);
    }

    /// <summary>
    /// In duplicates view, moves the cursor to the first image of the next
    /// group (higher group id). Returns false if already in the last group or
    /// not in duplicates view. Records in the view are contiguous by group and
    /// ordered by ascending group id.
    /// </summary>
    public bool MoveToNextGroup()
    {
        if (!HasDuplicatesSubset)
            return false;

        var records = _store.Records;
        var currentIndex = _store.CurrentIndex;
        if ((uint)currentIndex >= (uint)records.Count)
            return false;

        var currentGroup = records[currentIndex].GroupId;

        var next = currentIndex + 1;
        while (next < records.Count && records[next].GroupId == currentGroup)
            next++;

        if (next >= records.Count)
            return false;

        _store.MoveToIndex(next);
        return true;
    }

    /// <summary>
    /// In duplicates view, moves the cursor to the first image of the previous
    /// group (lower group id). Returns false if already in the first group or
    /// not in duplicates view.
    /// </summary>
    public bool MoveToPreviousGroup()
    {
        if (!HasDuplicatesSubset)
            return false;

        var records = _store.Records;
        var currentIndex = _store.CurrentIndex;
        if ((uint)currentIndex >= (uint)records.Count)
            return false;

        var currentGroup = records[currentIndex].GroupId;

        // Walk back to the first record of the current group.
        var start = currentIndex;
        while (start > 0 && records[start - 1].GroupId == currentGroup)
            start--;

        if (start == 0)
            return false; // already in the first group

        // The record just before the current group belongs to the previous
        // group; walk back to that group's first record.
        var previousGroup = records[start - 1].GroupId;
        var target = start - 1;
        while (target > 0 && records[target - 1].GroupId == previousGroup)
            target--;

        _store.MoveToIndex(target);
        return true;
    }

    public void Purge(string filePath)
    {
        if (_store.Remove(filePath))
            _version++;
    }

    public bool HasNext() => _store.Count > 0 && _store.CurrentIndex < _store.Count - 1;

    public bool HasPrevious() => _store.CurrentIndex > 0;

    public bool IsFirst() => _store.Count > 0 && _store.CurrentIndex == 0;

    public bool IsLast() => _store.Count > 0 && _store.CurrentIndex == _store.Count - 1;

    /// <summary>
    /// Returns a slice of image file paths centered on the current image,
    /// including the current image and up to <paramref name="depth"/> images
    /// before and after it in the collection.
    /// </summary>
    public string[] GetRange(int depth) => _store.GetRange(depth);

    public Navigation GetNavigation()
    {
        var records = _store.Records;
        var currentIndex = _store.CurrentIndex;

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

    private (int Start, int End) GetContiguousDirBounds(int seedIndex, string normalizedDir)
    {
        var records = _store.Records;

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

    public string? GetTopDirectory() => _topDirectory;

    public CollectionType GetCollectionType()
    {
        return _store.Count == 0 ? CollectionType.Undefined : _collectionType;
    }

    /// <summary>
    /// Returns sorted unique normalized directory paths from the current records.
    /// </summary>
    public List<string> GetDirectoryPaths()
    {
        var records = _store.Records;
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
    public List<DirEntry> GetFullDirectoryTree()
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
    public DirectorySnapshot GetSnapshot() => new(_version, _topDirectory, GetFullDirectoryTree(), GetCurrentDirectory());

    /// <summary>
    /// Returns the normalized directory of the current image.
    /// </summary>
    public string? GetCurrentDirectory() => _store.Current?.Directory;

    /// <summary>
    /// Moves to the first image in the given directory.
    /// Parameter must be a normalized absolute directory path
    /// (same format as returned by GetDirectoryPaths).
    /// Returns true if a matching image was found.
    /// </summary>
    public bool MoveToFirstInDirectory(string normalizedDir)
    {
        var records = _store.Records;
        for (var i = 0; i < records.Count; i++)
        {
            if (PathComparer.Equals(records[i].Directory, normalizedDir))
            {
                _store.MoveToIndex(i);
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