using Lyra.FileLoader.Store;

namespace Lyra.FileLoader.Enumeration;

public sealed record CollectionLoadResult(
    IReadOnlyList<FileRecord> Files,
    IReadOnlyList<string> AllDirectories,
    bool? SingleDirectory,
    string? TopDirectory,
    FileDropContext DropContext,
    CollectionType CollectionType
)
{
    public static CollectionLoadResult Empty(FileDropContext dropContext) =>
        new([], [], null, null, dropContext, CollectionType.Undefined);
}