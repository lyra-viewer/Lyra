using Lyra.Common;
using Lyra.FileLoader.Store;

namespace Lyra.FileLoader.Duplicates.Perceptual;

/// <summary>
/// Finds visually-similar images by decoding thumbnails via the injected
/// <see cref="IThumbnailSource"/> (the dependency is for testability).
/// </summary>
public sealed class PerceptualDuplicateFinder(IThumbnailSource thumbnails)
{
    // Of 64 bits. Validated genuine duplicates (resize / re-encode) land at distance 0-4;
    // unrelated low-detail images (near-flat thumbnails) can collide around ~10. 5 keeps a
    // margin over real duplicates while rejecting those sparse-hash false positives.
    public const int DefaultMaxDistance = 5;

    public IReadOnlyList<PerceptualGroup> Scan(int maxDistance = DefaultMaxDistance, IProgress<PerceptualScanProgress>? progress = null, CancellationToken ct = default)
    {
        EnsureHashes(progress, ct);
        return Cluster(maxDistance, ct);
    }

    private static List<PerceptualGroup> Cluster(int maxDistance, CancellationToken ct)
    {
        var hashed = FileRecordDatabase.Records.ToArray().Where(r => r.HasPHash && IsEligible(r)).ToList();

        var uf = new UnionFind(hashed.Count);
        for (var i = 0; i < hashed.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            for (var j = i + 1; j < hashed.Count; j++)
            {
                if (PerceptualHash.Distance(hashed[i].PHash, hashed[j].PHash) <= maxDistance)
                    uf.Union(i, j);
            }
        }

        var byRoot = new Dictionary<int, List<FileRecord>>();
        for (var i = 0; i < hashed.Count; i++)
        {
            var root = uf.Find(i);
            if (!byRoot.TryGetValue(root, out var list))
                byRoot[root] = list = [];

            list.Add(hashed[i]);
        }

        return byRoot.Values.Where(g => g.Count > 1).Select(g => new PerceptualGroup(g)).ToList();
    }

    private void EnsureHashes(IProgress<PerceptualScanProgress>? progress, CancellationToken ct)
    {
        var snapshot = FileRecordDatabase.Records.ToArray();
        var total = snapshot.Count(IsEligible);

        var hashed = 0;
        foreach (var record in snapshot)
        {
            ct.ThrowIfCancellationRequested();

            if (!IsEligible(record))
                continue;

            if (!record.HasPHash)
            {
                var luma = thumbnails.GetLuma(record.Path, PerceptualHash.Width, PerceptualHash.Height, ct);
                if (luma is not null && luma.Length == PerceptualHash.SampleCount)
                    FileRecordDatabase.SetPHash(record.Path, PerceptualHash.Compute(luma));
            }

            hashed++;
            progress?.Report(new PerceptualScanProgress(hashed, total));
        }
    }

    private static bool IsEligible(FileRecord record) => ImageFormat.IsPerceptualHashSupported(record.Extension);
}