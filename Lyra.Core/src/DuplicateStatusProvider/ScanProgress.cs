namespace Lyra.DuplicateStatusProvider;

public enum ScanPhase
{
    None,
    Exact,      // size + content-hash duplicate scan
    Perceptual  // pHash scan
}

// Snapshot of a running duplicate scan, for the status overlay
public readonly record struct ScanProgress(
    bool Active,
    ScanPhase Phase,
    int Done,
    int Total
);
