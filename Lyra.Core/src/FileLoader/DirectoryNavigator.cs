using Lyra.PathUtils;

namespace Lyra.FileLoader;

public static class DirectoryNavigator
{
    private static CollectionType _collectionType = CollectionType.Undefined;

    private static List<string> _imageList = [];
    private static int _currentIndex = -1;
    private static bool? _singleDirectory;

    private static string? _topDirectory;

    public static void ApplyCollection(List<string> files, FileDropContext dropContext, bool? singleDirectory, string? topDirectory)
    {
        _topDirectory = topDirectory;
        _collectionType = DecideCollectionType(dropContext);

        ApplyCollectionInternal(files, dropContext.AnchorPath, singleDirectory);
    }

    private static void ApplyCollectionInternal(List<string> files, string? anchorCandidate, bool? singleDirectory)
    {
        string? newAnchor = null;

        if (!string.IsNullOrWhiteSpace(anchorCandidate))
            newAnchor = files.FirstOrDefault(f => PathComparer.Equals(f, anchorCandidate));

        newAnchor ??= files.Count > 0 ? files[0] : null;

        var newIndex = (newAnchor != null)
            ? files.FindIndex(f => PathComparer.Equals(f, newAnchor))
            : -1;

        _singleDirectory = singleDirectory;
        _imageList = files;
        _currentIndex = newIndex;
    }

    public static string? GetCurrent()
    {
        if (_imageList.Count > 0 && _currentIndex >= 0 && _currentIndex < _imageList.Count)
            return _imageList[_currentIndex];

        return null;
    }

    public static void MoveToNext()
    {
        if (_imageList.Count == 0 || _currentIndex < 0)
            return;

        if (_currentIndex < _imageList.Count - 1)
            _currentIndex++;
    }

    public static void MoveToPrevious()
    {
        if (_imageList.Count == 0)
            return;

        if (_currentIndex > 0)
            _currentIndex--;
    }

    public static void MoveToFirst()
    {
        if (_imageList.Count == 0)
            return;

        _currentIndex = 0;
    }

    public static void MoveToLast()
    {
        if (_imageList.Count == 0)
            return;

        _currentIndex = _imageList.Count - 1;
    }

    public static void MoveToLeftEdge() => MoveToDirEdge(goToStart: true);
    public static void MoveToRightEdge() => MoveToDirEdge(goToStart: false);

    private static void MoveToDirEdge(bool goToStart)
    {
        if (_imageList.Count == 0 || (uint)_currentIndex >= (uint)_imageList.Count)
            return;

        if (_singleDirectory is true)
        {
            if (goToStart) MoveToFirst();
            else MoveToLast();
            return;
        }

        var currentDir = GetNormalizedDir(_imageList[_currentIndex]);
        if (currentDir is null)
            return;

        var (start, end) = GetContiguousDirBounds(seedIndex: _currentIndex, normalizedDir: currentDir);

        var edge = goToStart ? start : end;
        if (_currentIndex != edge)
        {
            _currentIndex = edge;
            return;
        }

        // Already at edge → jump to neighbor directory's opposite edge
        var neighborProbe = goToStart ? start - 1 : end + 1;
        if ((uint)neighborProbe >= (uint)_imageList.Count)
        {
            if (goToStart) MoveToFirst();
            else MoveToLast();
            return;
        }

        var neighborDir = GetNormalizedDir(_imageList[neighborProbe]);
        if (neighborDir is null)
            return;

        var (ns, ne) = GetContiguousDirBounds(seedIndex: neighborProbe, normalizedDir: neighborDir);
        _currentIndex = goToStart ? ne : ns;
    }

    private static string? GetNormalizedDir(string filePath)
    {
        var dir = Path.GetDirectoryName(filePath);
        if (string.IsNullOrWhiteSpace(dir))
            return null;

        return Path.GetFullPath(dir)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }

    public static bool HasNext()
    {
        return _imageList.Count > 0 && _currentIndex < _imageList.Count - 1;
    }

    public static bool HasPrevious()
    {
        return _imageList.Count > 0 && _currentIndex > 0;
    }

    public static bool IsFirst()
    {
        return _imageList.Count > 0 && _currentIndex == 0;
    }

    public static bool IsLast()
    {
        return _imageList.Count > 0 && _currentIndex == _imageList.Count - 1;
    }

    /// <summary>
    /// Returns a slice of image file paths centered on the current image,
    /// including the current image and up to <paramref name="depth"/> images
    /// before and after it in the collection.
    /// </summary>
    /// <param name="depth">
    /// The number of images to include before and after the current index.
    /// For example, a depth of 2 will return up to 5 items: two before, the current, and two after.
    /// </param>
    /// <returns>
    /// An array of image file paths representing the window around the current image.
    /// If the collection is empty or no current index is set, returns an empty array.
    /// </returns>
    public static string[] GetRange(int depth)
    {
        if (depth < 0 || _imageList.Count == 0 || (uint)_currentIndex >= (uint)_imageList.Count)
            return [];

        var start = Math.Max(0, _currentIndex - depth);
        var end = Math.Min(_imageList.Count - 1, _currentIndex + depth);

        var result = new string[end - start + 1];
        for (int i = start, j = 0; i <= end; i++, j++)
            result[j] = _imageList[i];

        return result;
    }

    public static Navigation GetNavigation()
    {
        var navigation = new Navigation
        {
            CollectionCount = _imageList.Count,
            CollectionIndex = _currentIndex + 1,
            DirectoryCount = null,
            DirectoryIndex = null
        };

        if (GetCollectionType() != CollectionType.MultiDirectorySelection || _imageList.Count == 0 || (uint)_currentIndex >= (uint)_imageList.Count)
            return navigation;

        var currentDir = GetNormalizedDir(_imageList[_currentIndex]);
        if (currentDir is null)
            return navigation;

        var (start, end) = GetContiguousDirBounds(_currentIndex, currentDir);

        navigation.DirectoryCount = end - start + 1;
        navigation.DirectoryIndex = _currentIndex - start + 1;
        return navigation;
    }

    private static (int Start, int End) GetContiguousDirBounds(int seedIndex, string normalizedDir)
    {
        var start = seedIndex;
        while (start > 0)
        {
            var dir = GetNormalizedDir(_imageList[start - 1]);
            if (dir is null || !PathComparer.Equals(dir, normalizedDir))
                break;

            start--;
        }

        var end = seedIndex;
        while (end < _imageList.Count - 1)
        {
            var dir = GetNormalizedDir(_imageList[end + 1]);
            if (dir is null || !PathComparer.Equals(dir, normalizedDir))
                break;

            end++;
        }

        return (start, end);
    }

    public static string? GetTopDirectory() => _topDirectory;

    public static CollectionType GetCollectionType()
    {
        return _imageList.Count == 0 ? CollectionType.Undefined : _collectionType;
    }

    private static CollectionType DecideCollectionType(FileDropContext ctx)
    {
        // SingleDirectoryCollection ("open with" semantics)
        if (ctx.ExplicitFiles.Count == 1 && ctx.ExplicitDirectories.Count == 0 && ctx.IsSingleFileOpen)
            return CollectionType.SingleDirectoryCollection;

        // SingleDirectoryCollection (one directory if no subdirs, otherwise multi)
        if (ctx.ExplicitDirectories.Count == 1 && ctx.ExplicitFiles.Count == 0)
        {
            return PathUtils.PathUtils.DirectoryHasSubdirectories(ctx.ExplicitDirectories[0])
                ? CollectionType.MultiDirectorySelection
                : CollectionType.SingleDirectoryCollection;
        }

        // SingleDirectorySelection (many files, same directory) 
        if (ctx.ExplicitFiles.Count > 1 && ctx.ExplicitDirectories.Count == 0 && ctx.IsSameDirectoryGroup)
            return CollectionType.SingleDirectorySelection;

        // MultiDirectorySelection
        return CollectionType.MultiDirectorySelection;
    }

    public record struct Navigation
    {
        public int CollectionCount;
        public int CollectionIndex;
        public int? DirectoryIndex;
        public int? DirectoryCount;
    }
}