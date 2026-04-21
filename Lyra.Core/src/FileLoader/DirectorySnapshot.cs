namespace Lyra.FileLoader;

public readonly record struct DirectorySnapshot(
    int Version,
    string? TopDirectory,
    IReadOnlyList<DirEntry> Entries,
    string? CurrentDirectory
);