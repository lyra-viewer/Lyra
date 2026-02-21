namespace Lyra.DropStatusProvider;

public sealed class DropProgressTracker : IDropProgressProvider
{
    // 0/1
    private int _active; 
    private int _aborted;
    
    private long _paths;
    private long _files;
    private long _supported;

    public void Start()
    {
        Reset();
        Volatile.Write(ref _aborted, 0);
        Volatile.Write(ref _active, 1);
    }

    public void Finish()
    {
        Volatile.Write(ref _active, 0);
    }

    public void MarkAborted()
    {
        Volatile.Write(ref _aborted, 1);
        Volatile.Write(ref _active, 0);
    }

    public void Reset()
    {
        Interlocked.Exchange(ref _paths, 0);
        Interlocked.Exchange(ref _files, 0);
        Interlocked.Exchange(ref _supported, 0);
    }

    public void AddPaths(long count = 1) { if (count > 0) Interlocked.Add(ref _paths, count); }
    public void AddFiles(long count = 1) { if (count > 0) Interlocked.Add(ref _files, count); }
    public void AddSupported(long count = 1) { if (count > 0) Interlocked.Add(ref _supported, count); }

    public DropProgress GetDropStatus()
        => new(
            Active: Volatile.Read(ref _active) == 1,
            Aborted: Volatile.Read(ref _aborted) == 1,
            PathsEnqueued: Volatile.Read(ref _paths),
            FilesEnumerated: Volatile.Read(ref _files),
            FilesSupported: Volatile.Read(ref _supported)
        );
}