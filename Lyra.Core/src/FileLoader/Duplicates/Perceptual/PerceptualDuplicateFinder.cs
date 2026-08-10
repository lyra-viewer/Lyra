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

    // Half the cores: the scan runs while the viewer keeps decoding and drawing, so it leaves
    // room rather than taking the machine.
    private static readonly int DegreeOfParallelism = Math.Max(1, Environment.ProcessorCount / 2);

    private const int HashBits = 64;

    public IReadOnlyList<PerceptualGroup> Scan(int maxDistance = DefaultMaxDistance, IProgress<PerceptualScanProgress>? progress = null, CancellationToken ct = default)
    {
        EnsureHashes(progress, ct);
        return Cluster(maxDistance, ct);
    }

    private static List<PerceptualGroup> Cluster(int maxDistance, CancellationToken ct)
    {
        if (maxDistance < 0)
            return [];

        var hashed = FileRecordDatabase.Records.ToArray().Where(r => r.HasPHash && IsEligible(r)).ToList();

        var hashes = hashed.Select(r => r.PHash).ToArray();
        var uf = new UnionFind(hashes.Length);

        var representatives = MergeIdenticalHashes(hashes, uf);
        MergeNearHashes(hashes, representatives, uf, maxDistance, ct);

        return GroupByRoot(hashed, uf);
    }

    /// <summary>Merges exact matches and returns one index per distinct hash.</summary>
    private static List<int> MergeIdenticalHashes(ulong[] hashes, UnionFind uf)
    {
        var representatives = new List<int>();
        var firstIndexOfHash = new Dictionary<ulong, int>();

        for (var i = 0; i < hashes.Length; i++)
        {
            if (firstIndexOfHash.TryGetValue(hashes[i], out var first))
            {
                uf.Union(first, i);
                continue;
            }

            firstIndexOfHash[hashes[i]] = i;
            representatives.Add(i);
        }

        return representatives;
    }

    /// <summary>
    /// Merges hashes within <paramref name="maxDistance"/> bits of each other. Two such hashes
    /// differ in at most maxDistance bands, so slicing into maxDistance + 1 bands leaves at least
    /// one identical (pigeonhole): grouping by band value yields every near pair as a candidate,
    /// which is why this stays exact rather than approximate.
    /// </summary>
    private static void MergeNearHashes(ulong[] hashes, List<int> representatives, UnionFind uf, int maxDistance, CancellationToken ct)
    {
        var buckets = new Dictionary<ulong, List<int>>();

        foreach (var mask in BandMasks(maxDistance + 1))
        {
            ct.ThrowIfCancellationRequested();

            buckets.Clear();
            foreach (var index in representatives)
            {
                var key = hashes[index] & mask;
                if (!buckets.TryGetValue(key, out var bucket))
                    buckets[key] = bucket = [];

                bucket.Add(index);
            }

            foreach (var bucket in buckets.Values)
            {
                if (bucket.Count > 1)
                    MergeWithinBucket(hashes, bucket, uf, maxDistance);
            }
        }
    }

    private static void MergeWithinBucket(ulong[] hashes, List<int> bucket, UnionFind uf, int maxDistance)
    {
        for (var a = 0; a < bucket.Count; a++)
        for (var b = a + 1; b < bucket.Count; b++)
        {
            if (PerceptualHash.Distance(hashes[bucket[a]], hashes[bucket[b]]) <= maxDistance)
                uf.Union(bucket[a], bucket[b]);
        }
    }

    /// <summary>Splits the hash into <paramref name="count"/> contiguous slices, widest first.</summary>
    private static IEnumerable<ulong> BandMasks(int count)
    {
        count = Math.Clamp(count, 1, HashBits);

        var offset = 0;
        for (var band = 0; band < count; band++)
        {
            var width = HashBits / count + (band < HashBits % count ? 1 : 0);
            yield return width >= HashBits ? ulong.MaxValue : ((1UL << width) - 1) << offset;
            offset += width;
        }
    }

    private static List<PerceptualGroup> GroupByRoot(List<FileRecord> hashed, UnionFind uf)
    {
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
        var pending = new List<string>();
        var eligible = 0;
        foreach (var record in FileRecordDatabase.Records.ToArray())
        {
            if (!IsEligible(record))
                continue;

            eligible++;
            if (!record.HasPHash)
                pending.Add(record.Path);
        }

        if (eligible == 0)
            return;

        var reporter = new ProgressReporter(progress, eligible, done: eligible - pending.Count);
        reporter.Publish();

        // FileRecordDatabase is not thread-safe, so the workers only fill a local array and the
        // store is written from this thread once they are done.
        var hashes = new ulong[pending.Count];
        var options = new ParallelOptions { CancellationToken = ct, MaxDegreeOfParallelism = DegreeOfParallelism };

        try
        {
            Parallel.For(0, pending.Count, options, i =>
            {
                var luma = thumbnails.GetLuma(pending[i], PerceptualHash.Width, PerceptualHash.Height, ct);
                if (luma is not null && luma.Length == PerceptualHash.SampleCount)
                    hashes[i] = PerceptualHash.Compute(luma); // never 0, see PerceptualHash.ComputedMarker

                reporter.Advance();
            });
        }
        finally
        {
            for (var i = 0; i < pending.Count; i++)
            {
                if (hashes[i] != 0)
                    FileRecordDatabase.SetPHash(pending[i], hashes[i]);
            }
        }
    }

    /// <summary>
    /// Counts and reports under one lock: workers finish out of order, so doing the two
    /// separately lets the reported count go backwards.
    /// </summary>
    private sealed class ProgressReporter(IProgress<PerceptualScanProgress>? progress, int total, int done)
    {
        private readonly Lock _gate = new();
        private int _done = done;

        public void Advance() => Report(advance: true);

        public void Publish() => Report(advance: false);

        private void Report(bool advance)
        {
            if (progress is null)
                return;

            lock (_gate)
            {
                if (advance)
                    _done++;

                progress.Report(new PerceptualScanProgress(_done, total));
            }
        }
    }

    private static bool IsEligible(FileRecord record) => ImageFormat.IsPerceptualHashSupported(record.Extension);
}