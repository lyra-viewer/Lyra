using Lyra.Imaging.Content.Textures;
using Lyra.ManagedCodecs.Scene;

namespace Lyra.Imaging.Decoding.Decoders.Model;

/// <summary>
/// Finds the files a model refers to. Tries every stored path where it points, relative to the
/// model when it is not rooted, then a file of the same name beside the model, in
/// <c>&lt;model&gt;.fbm</c> or in <c>textures</c>. Stored paths are often absolute paths from the
/// author's machine, possibly another OS, so the name is taken after either kind of separator.
/// </summary>
internal sealed class ReferenceResolver
{
    private readonly string _modelDirectory;
    private readonly string[] _fallbackDirectories;
    private readonly Dictionary<string, string[]> _listings = new(StringComparer.Ordinal);

    public ReferenceResolver(string modelPath)
    {
        var full = Path.GetFullPath(modelPath);
        _modelDirectory = Path.GetDirectoryName(full) ?? "";

        _fallbackDirectories =
        [
            _modelDirectory,
            Path.Combine(_modelDirectory, Path.GetFileNameWithoutExtension(full) + ".fbm"),
            Path.Combine(_modelDirectory, "textures")
        ];
    }

    public TextureSource Resolve(FileReference reference)
    {
        var usable = reference.Candidates
            .Where(candidate => !string.IsNullOrWhiteSpace(candidate) && !candidate.Contains("://", StringComparison.Ordinal))
            .ToList();

        foreach (var candidate in usable)
        {
            if (AtStoredPath(candidate) is { } found)
                return new TextureSource(TextureLocation.Linked, reference.StoredPath, found);
        }

        foreach (var name in usable.Select(FileName).Where(name => name.Length > 0))
        {
            foreach (var directory in _fallbackDirectories)
            {
                if (Find(directory, name) is { } found)
                    return new TextureSource(TextureLocation.FoundByName, reference.StoredPath, found);
            }
        }

        return new TextureSource(TextureLocation.Missing, reference.StoredPath, null);
    }

    private string? AtStoredPath(string storedPath)
    {
        var native = storedPath.Replace(Path.DirectorySeparatorChar == '/' ? '\\' : '/', Path.DirectorySeparatorChar);

        try
        {
            var candidate = Path.IsPathRooted(native) ? native : Path.Combine(_modelDirectory, native);
            return File.Exists(candidate) ? Path.GetFullPath(candidate) : null;
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }
    }

    private static string FileName(string storedPath)
    {
        var cut = storedPath.LastIndexOfAny(['/', '\\']);
        return storedPath[(cut + 1)..].Trim();
    }

    private string? Find(string directory, string name)
    {
        var files = Listing(directory);
        return files.FirstOrDefault(file => Path.GetFileName(file).Equals(name, StringComparison.Ordinal))
               ?? files.FirstOrDefault(file => Path.GetFileName(file).Equals(name, StringComparison.OrdinalIgnoreCase));
    }

    private string[] Listing(string directory)
    {
        if (_listings.TryGetValue(directory, out var files))
            return files;

        try
        {
            files = Directory.Exists(directory) ? Directory.GetFiles(directory) : [];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            files = [];
        }

        _listings[directory] = files;
        return files;
    }
}