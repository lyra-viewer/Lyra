using Lyra.Common;
using Lyra.PathUtils;

namespace Lyra.FileLoader.Store;

/// <summary>
/// In-memory store for the current collection of <see cref="FileRecord"/>s plus a
/// single cursor. It only stores, sorts, and remembers position (prev/next/seek);
/// </summary>
public static class FileRecordDatabase
{
    private static List<FileRecord> _records = [];

    private static readonly Dictionary<string, int> IndexByPath = new(PathComparer.CommonPathComparer);

    private static int _index = -1;

    // Duplicates view: when active, _records holds only the grouped subset and the full
    // collection is stashed here for restoration on exit.
    private static List<FileRecord>? _stashedRecords;
    private static int _stashedIndex;

    public static bool InDuplicatesView => _stashedRecords is not null;

    public static IReadOnlyList<FileRecord> Records => _records;
    public static int Count => _records.Count;
    public static int CurrentIndex => _index;
    public static FileRecord? Current => (uint)_index < (uint)_records.Count ? _records[_index] : null;

    public static void Load(IEnumerable<FileRecord> records, IComparer<FileRecord>? comparer = null, string? currentPath = null)
    {
        _stashedRecords = null;

        _records = records as List<FileRecord> ?? [.. records];

        if (comparer != null)
            _records.Sort(comparer);

        RebuildIndexMap();

        _index = currentPath != null && IndexByPath.TryGetValue(currentPath, out var i) ? i : (_records.Count > 0 ? 0 : -1);
    }

    /// <summary>Re-sorts in place, preserving the current record by path.</summary>
    public static void Sort(IComparer<FileRecord> comparer)
    {
        if (_records.Count == 0)
            return;

        var currentPath = Current?.Path;
        _records.Sort(comparer);
        RebuildIndexMap();

        _index = currentPath != null && IndexByPath.TryGetValue(currentPath, out var i) ? i : 0;
    }

    /// <summary>Lazily records a perceptual hash after decode. Returns false if the path is gone.</summary>
    public static bool SetPHash(string path, ulong pHash) => UpdateSlot(path, r => r with { PHash = pHash });

    /// <summary>Lazily records a file size. Returns false if the path is gone.</summary>
    public static bool SetSize(string path, long size) => UpdateSlot(path, r => r with { Size = size });

    /// <summary>Lazily records an exact-content hash. Returns false if the path is gone.</summary>
    public static bool SetContentHash(string path, UInt128 hash) => UpdateSlot(path, r => r with { ContentHash = hash });

    /// <summary>Assigns (or clears, with null) a duplicate-group id. Returns false if the path is gone.</summary>
    public static bool SetGroupId(string path, int? groupId) => UpdateSlot(path, r => r with { GroupId = groupId });

    /// <summary>Clears all group ids in the active collection. Not valid while in the duplicates view.</summary>
    public static void ClearGroups()
    {
        for (var i = 0; i < _records.Count; i++)
        {
            if (_records[i].GroupId.HasValue)
                _records[i] = _records[i] with { GroupId = null };
        }
    }

    /// <summary>
    /// Switches to a view of only the grouped records (sorted by group id), stashing the full
    /// collection. Returns false (and stays unchanged) if nothing is grouped. Idempotent.
    /// </summary>
    public static bool EnterDuplicatesView()
    {
        if (InDuplicatesView)
            return true;

        var subset = _records
            .Where(r => r.HasGroup)
            .OrderBy(r => r.GroupId!.Value)
            .ThenBy(r => r.Name, NaturalStringComparer.Instance)
            .ToList();

        if (subset.Count == 0)
            return false;

        _stashedRecords = _records;
        _stashedIndex = _index;

        _records = subset;
        RebuildIndexMap();
        _index = 0;
        return true;
    }

    /// <summary>Restores the full stashed collection.</summary>
    public static void ExitDuplicatesView()
    {
        if (_stashedRecords is null)
            return;

        _records = _stashedRecords;
        _stashedRecords = null;
        RebuildIndexMap();
        _index = _records.Count == 0 ? -1 : Math.Clamp(_stashedIndex, 0, _records.Count - 1);
    }

    public static void MoveNext()
    {
        if (_index >= 0 && _index < _records.Count - 1)
            _index++;
    }

    public static void MovePrevious()
    {
        if (_index > 0)
            _index--;
    }

    public static void MoveToFirst()
    {
        if (_records.Count > 0)
            _index = 0;
    }

    public static void MoveToLast()
    {
        if (_records.Count > 0)
            _index = _records.Count - 1;
    }

    /// <summary>Sets the cursor to an explicit index, clamped to the valid range.</summary>
    public static void MoveToIndex(int index)
    {
        if (_records.Count == 0)
            return;

        _index = Math.Clamp(index, 0, _records.Count - 1);
    }

    /// <summary>Seeks the cursor to a record by path. Returns false if not found.</summary>
    public static bool MoveTo(string path)
    {
        if (!IndexByPath.TryGetValue(path, out var i))
            return false;

        _index = i;
        return true;
    }

    /// <summary>
    /// Removes a record by path and keeps the cursor pointing at a sensible neighbor.
    /// Returns false if the path was not present.
    /// </summary>
    public static bool Remove(string path)
    {
        if (!IndexByPath.TryGetValue(path, out var idx))
            return false;

        _records.RemoveAt(idx);
        RebuildIndexMap();

        if (_records.Count == 0)
            _index = -1;
        else if (idx < _index)
            _index--;
        else if (idx == _index)
            _index = Math.Min(_index, _records.Count - 1);

        return true;
    }

    /// <summary>
    /// Returns up to <paramref name="depth"/> paths on each side of the cursor plus
    /// the current one - the preload/cleanup window. Empty when there is no cursor.
    /// </summary>
    public static string[] GetRange(int depth)
    {
        if (depth < 0 || (uint)_index >= (uint)_records.Count)
            return [];

        var start = Math.Max(0, _index - depth);
        var end = Math.Min(_records.Count - 1, _index + depth);

        var result = new string[end - start + 1];
        for (int i = start, j = 0; i <= end; i++, j++)
            result[j] = _records[i].Path;

        return result;
    }

    private static bool UpdateSlot(string path, Func<FileRecord, FileRecord> update)
    {
        if (!IndexByPath.TryGetValue(path, out var i))
            return false;

        _records[i] = update(_records[i]);
        return true;
    }

    private static void RebuildIndexMap()
    {
        IndexByPath.Clear();
        for (var i = 0; i < _records.Count; i++)
            IndexByPath[_records[i].Path] = i;
    }
}