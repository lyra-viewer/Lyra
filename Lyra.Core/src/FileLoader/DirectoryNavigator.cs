using Lyra.Common;

namespace Lyra.FileLoader;

public static class DirectoryNavigator
{
    private static string? _anchorFile;

    private static List<string> _imageList = [];
    private static int _currentIndex = -1;
    private static bool? _singleDirectory;

    private static string? _topDirectory;

    private static readonly StringComparer PathComparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    public static void SearchImages(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || (!File.Exists(path) && !Directory.Exists(path)))
            throw new ArgumentException("[DirectoryNavigator] Invalid path!", nameof(path));

        var isDir = (File.GetAttributes(path) & FileAttributes.Directory) != 0;

        string currentDir;
        string? anchorCandidate = null;

        if (isDir)
        {
            currentDir = path;
        }
        else
        {
            currentDir = Path.GetDirectoryName(path) ?? throw new ArgumentException("[DirectoryNavigator] Invalid path!", nameof(path));
            anchorCandidate = path;
        }

        var files = FilePathProcessor.ProcessImagePaths([currentDir], isDir, out var singleDirectory, out _topDirectory);
        if (anchorCandidate is null && files.Count > 0)
            anchorCandidate = files[0];

        SetCollection(files, anchorCandidate, singleDirectory);

        Logger.Info($"[DirectoryNavigator] {_imageList.Count} images in directory.");
    }

    public static void SearchImages(List<string> paths)
    {
        if (paths is null || paths.Count == 0)
            throw new ArgumentException("[DirectoryNavigator] Invalid path list!", nameof(paths));

        var anchorCandidate = paths
            .Where(File.Exists)
            .FirstOrDefault(p => ImageFormat.IsSupported(Path.GetExtension(p)));

        var files = FilePathProcessor.ProcessImagePaths(paths, recurseSubdirs: null, out var singleDirectory, out _topDirectory);

        if (anchorCandidate is null && files.Count > 0)
            anchorCandidate = files[0];

        SetCollection(files, anchorCandidate, singleDirectory);

        Logger.Info($"[DirectoryNavigator] {_imageList.Count} files in collection.");
    }

    private static void SetCollection(List<string> files, string? anchorCandidate, bool? singleDirectory)
    {
        string? newAnchor = null;

        if (anchorCandidate != null)
            newAnchor = files.FirstOrDefault(f => PathComparer.Equals(f, anchorCandidate));

        newAnchor ??= files.Count > 0 ? files[0] : null;

        var newIndex = (newAnchor != null) ? files.FindIndex(f => PathComparer.Equals(f, newAnchor)) : -1;

        _singleDirectory = singleDirectory;
        _anchorFile = newAnchor;
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
        List<string> paths = [];

        if (depth < 0 || _imageList.Count == 0 || _currentIndex < 0)
            return paths.ToArray();

        var start = Math.Max(0, _currentIndex - depth);
        var end = Math.Min(_imageList.Count - 1, _currentIndex + depth);

        for (var i = start; i <= end; i++)
            paths.Add(_imageList[i]);

        return paths.ToArray();
    }

    public static Navigation GetNavigation()
    {
        var navigation = new Navigation()
        {
            CollectionCount = _imageList.Count,
            CollectionIndex = _currentIndex + 1,
            DirectoryCount = null,
            DirectoryIndex = null
        };

        if (GetCollectionType() == CollectionType.MultiDirectorySelection &&
            _currentIndex >= 0 &&
            _currentIndex < _imageList.Count)
        {
            var currentFile = _imageList[_currentIndex];
            var currentDir = Path.GetDirectoryName(currentFile);

            if (!string.IsNullOrEmpty(currentDir))
            {
                var normalizedCurrentDir = Path.GetFullPath(currentDir).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                var imagesInDir = _imageList
                    .Select(f => new { File = f, Dir = Path.GetDirectoryName(f) })
                    .Where(x =>
                        !string.IsNullOrEmpty(x.Dir) &&
                        PathComparer.Equals(
                            Path.GetFullPath(x.Dir!).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                            normalizedCurrentDir))
                    .Select(x => x.File)
                    .ToList();

                navigation.DirectoryCount = imagesInDir.Count;
                navigation.DirectoryIndex = imagesInDir.FindIndex(f => PathComparer.Equals(f, currentFile)) + 1;
            }
        }

        return navigation;
    }

    public static string? GetTopDirectory()
    {
        return _topDirectory;
    }

    public static CollectionType GetCollectionType()
    {
        if (_imageList.Count == 0 || _singleDirectory is null)
            return CollectionType.Undefined;

        if (_singleDirectory == true)
            return _anchorFile is not null
                ? CollectionType.SingleDirectoryCollection
                : CollectionType.SingleDirectorySelection;

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