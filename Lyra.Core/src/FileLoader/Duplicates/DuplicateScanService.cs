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

    public IScanProgressProvider Progress => _tracker;

    /// <summary>Raised on the scan thread when a scan finishes, with the number of duplicate groups found.</summary>
    public event Action<int>? Completed;

    /// <summary>Starts a scan on a background thread. No-op if one is already running.</summary>
    public void Start()
    {
        if (_task is { IsCompleted: false })
            return;

        _task = Task.Run(Run);
    }

    private void Run()
    {
        _tracker.Start();
        var groupCount = 0;
        try
        {
            FileRecordDatabase.ClearGroups();

            var exact = DuplicateFinder.Scan(progress: _tracker);
            var perceptual = new PerceptualDuplicateFinder(thumbnails).Scan(progress: _tracker);

            groupCount = AssignGroups(exact, perceptual);
            Logger.Info($"[Duplicates] {groupCount} group(s) from {exact.Count} exact + {perceptual.Count} perceptual cluster(s).");
        }
        catch (Exception ex)
        {
            Logger.Error($"[Duplicates] Scan failed: {ex}");
        }
        finally
        {
            _tracker.Finish();
            Completed?.Invoke(groupCount);
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