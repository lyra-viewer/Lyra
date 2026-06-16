using Lyra.FileLoader.Store;

namespace Lyra.FileLoader.Duplicates.Exact;

public sealed record DuplicateGroup(long Size, UInt128 ContentHash, IReadOnlyList<FileRecord> Files);
