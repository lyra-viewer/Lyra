namespace Lyra.FileLoader;

public record struct FileDropContext(
    IReadOnlyList<string> ExplicitPaths,
    IReadOnlyList<string> ExplicitFiles,
    IReadOnlyList<string> ExplicitDirectories,
    bool IsSameDirectoryGroup,
    string? AnchorPath,
    bool IsSingleFileOpen
);