namespace Lyra.FileLoader;

public record DirEntry(
    string Path,
    bool HasImages,
    string? CollapsedDisplayName = null
);