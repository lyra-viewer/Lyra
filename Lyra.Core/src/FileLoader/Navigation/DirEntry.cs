namespace Lyra.FileLoader.Navigation;

public record DirEntry(
    string Path,
    bool HasImages,
    string? CollapsedDisplayName = null
);