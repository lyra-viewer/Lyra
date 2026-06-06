namespace Lyra.FileLoader.Duplicates.Exact;

public readonly record struct DuplicateScanProgress(int FilesSized, int FilesHashed, int TotalCandidates);
