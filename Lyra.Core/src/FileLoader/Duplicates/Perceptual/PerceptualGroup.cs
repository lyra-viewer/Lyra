using Lyra.FileLoader.Store;

namespace Lyra.FileLoader.Duplicates.Perceptual;

public sealed record PerceptualGroup(IReadOnlyList<FileRecord> Files);
