using Lyra.Common;
using Lyra.DuplicateStatusProvider;
using Lyra.FileLoader.Duplicates.Exact;
using Lyra.FileLoader.Duplicates.Perceptual;
using Lyra.FileLoader.Store;

namespace Lyra.FileLoader.Duplicates;

/// <summary>
/// Runs the duplicate-scan phases (exact: size -> content hash; perceptual: pHash) over the
/// currently indexed collection, off the calling thread, and assigns a shared GroupId to every
/// file that ends up in the same cluster (exact and/or perceptual edges merged).
/// </summary>
public sealed class DuplicateScanService(IThumbnailSource thumbnails)
{
    private readonly ScanProgressTracker _tracker = new();
    private Task? _task;
    private CancellationTokenSource? _cts;
    private int _hashTolerance = PerceptualDuplicateFinder.DefaultMaxDistance;

    public IScanProgressProvider Progress => _tracker;

    /// <summary>
    /// When true, only the exact phase (size + content hash) runs and perceptual (pHash)
    /// similarity is skipped, so groups contain byte-identical files only.
    /// </summary>
    public bool ExactOnly { get; set; }

    public int HashTolerance
    {
        get => _hashTolerance;
        set => _hashTolerance = Math.Clamp(value, 1, 9);
    }

    public event Action<int>? Completed;

    public event Action? Aborted;

    public bool IsScanning => _task is { IsCompleted: false };

    public void Start()
    {
        if (IsScanning)
            return;

        _cts?.Dispose();
        _cts = new CancellationTokenSource();

        var ct = _cts.Token;
        _task = Task.Run(() => RunScan(ct), ct);
    }

    /// <summary>
    /// Requests cancellation of a running scan. Returns false when there is nothing to cancel or
    /// cancellation was already requested, so a caller like Esc can fall through to its normal
    /// action instead of being swallowed by a scan that refuses to unwind.
    /// </summary>
    public bool CancelIfRunning()
    {
        if (!IsScanning || _cts is not { IsCancellationRequested: false })
            return false;

        Logger.Info("[Duplicates] Cancelling scan.");
        Cancel();
        
        _tracker.MarkAborted();
        return true;
    }

    /// <summary>
    /// Cancels any running scan and waits briefly for it to unwind, so application shutdown does
    /// not race the scan still writing into the file record store.
    /// </summary>
    public void Shutdown()
    {
        Cancel();

        try
        {
            _task?.Wait(TimeSpan.FromSeconds(2));
        }
        catch (Exception ex)
        {
            Logger.Warning($"[Duplicates] Scan did not stop cleanly: {ex.Message}");
        }
    }

    private void Cancel()
    {
        try
        {
            _cts?.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // Already torn down
        }
    }

    private void RunScan(CancellationToken ct)
    {
        _tracker.Start();
        var groupCount = 0;
        var cancelled = false;
        try
        {
            FileRecordDatabase.ClearGroups();

            var exact = DuplicateFinder.Scan(progress: _tracker, ct);
            var perceptual = ExactOnly
                ? (IReadOnlyList<PerceptualGroup>)[]
                : new PerceptualDuplicateFinder(thumbnails).Scan(maxDistance: _hashTolerance, progress: _tracker, ct);

            groupCount = AssignGroups(exact, perceptual);
            Logger.Info($"[Duplicates] {groupCount} group(s) from {exact.Count} exact + {perceptual.Count} perceptual cluster(s).");
        }
        catch (OperationCanceledException)
        {
            cancelled = true;
            Logger.Info("[Duplicates] Scan aborted.");
        }
        catch (Exception ex)
        {
            Logger.Error($"[Duplicates] Scan failed: {ex}");
        }
        finally
        {
            if (cancelled)
            {
                _tracker.MarkAborted();
                Aborted?.Invoke();
            }
            else
            {
                _tracker.Finish();
                Completed?.Invoke(groupCount);
            }
        }
    }

    /// <summary>
    /// Merges exact and perceptual clusters into connected components (a file that is an exact
    /// duplicate of A and perceptually similar to B lands in one group), then writes an
    /// incrementing GroupId onto every member. Returns the number of groups.
    /// </summary>
    private static int AssignGroups(IReadOnlyList<DuplicateGroup> exact, IReadOnlyList<PerceptualGroup> perceptual)
    {
        var indexOf = new Dictionary<string, int>(PathComparer.CommonPathComparer);
        var paths = new List<string>();

        int Index(string path)
        {
            if (indexOf.TryGetValue(path, out var existing))
                return existing;

            var i = paths.Count;
            indexOf[path] = i;
            paths.Add(path);
            return i;
        }

        var clusters = new List<List<int>>();
        foreach (var g in exact)
            clusters.Add(g.Files.Select(f => Index(f.Path)).ToList());

        foreach (var g in perceptual)
            clusters.Add(g.Files.Select(f => Index(f.Path)).ToList());

        var uf = new UnionFind(paths.Count);
        foreach (var cluster in clusters)
            for (var k = 1; k < cluster.Count; k++)
                uf.Union(cluster[0], cluster[k]);

        // Every path here came from a cluster of 2+, so every component is a real group.
        var rootToGroup = new Dictionary<int, int>();
        var nextId = 1;
        for (var i = 0; i < paths.Count; i++)
        {
            var root = uf.Find(i);
            if (!rootToGroup.TryGetValue(root, out var gid))
                rootToGroup[root] = gid = nextId++;

            FileRecordDatabase.SetGroupId(paths[i], gid);
        }

        return rootToGroup.Count;
    }
}