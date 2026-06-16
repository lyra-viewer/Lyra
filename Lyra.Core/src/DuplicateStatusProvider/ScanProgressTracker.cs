using Lyra.FileLoader.Duplicates.Exact;
using Lyra.FileLoader.Duplicates.Perceptual;

namespace Lyra.DuplicateStatusProvider;

public sealed class ScanProgressTracker
    : IScanProgressProvider, IProgress<DuplicateScanProgress>, IProgress<PerceptualScanProgress>
{
    private int _active;
    private int _phase;
    private int _done;
    private int _total;

    public void Start()
    {
        Volatile.Write(ref _phase, (int)ScanPhase.None);
        Volatile.Write(ref _done, 0);
        Volatile.Write(ref _total, 0);
        Volatile.Write(ref _active, 1);
    }

    public void Finish() => Volatile.Write(ref _active, 0);

    void IProgress<DuplicateScanProgress>.Report(DuplicateScanProgress value)
        => Set(ScanPhase.Exact, value.FilesHashed, value.TotalCandidates);

    void IProgress<PerceptualScanProgress>.Report(PerceptualScanProgress value)
        => Set(ScanPhase.Perceptual, value.FilesHashed, value.Total);

    public ScanProgress GetScanStatus()
        => new(
            Active: Volatile.Read(ref _active) == 1,
            Phase: (ScanPhase)Volatile.Read(ref _phase),
            Done: Volatile.Read(ref _done),
            Total: Volatile.Read(ref _total)
        );

    private void Set(ScanPhase phase, int done, int total)
    {
        Volatile.Write(ref _phase, (int)phase);
        Volatile.Write(ref _done, done);
        Volatile.Write(ref _total, total);
    }
}