using Lyra.FileLoader.Duplicates.Perceptual;
using Lyra.FileLoader.Store;
using Xunit;

namespace Lyra.Core.Tests.Duplicates;

[Collection("FileRecordDatabase")]
public sealed class PerceptualDuplicateFinderTests
{
    /// <summary>The scanner hashes in parallel, so the call log needs its own lock.</summary>
    private sealed class FakeThumbnailSource : IThumbnailSource
    {
        private readonly Lock _gate = new();
        private readonly List<string> _calls = [];

        public Func<string, byte[]?> Provider = _ => new byte[PerceptualHash.SampleCount];

        public IReadOnlyList<string> Calls
        {
            get
            {
                lock (_gate)
                    return _calls.ToArray();
            }
        }

        public byte[]? GetLuma(string path, int width, int height, CancellationToken ct = default)
        {
            lock (_gate)
                _calls.Add(path);

            return Provider(path);
        }
    }

    private static FileRecord Rec(string path, ulong pHash = 0) =>
        new(path, Path.GetFileName(path), Path.GetDirectoryName(path) ?? string.Empty, PHash: pHash);

    private static byte[] DistinctLuma(int seed)
    {
        var luma = new byte[PerceptualHash.SampleCount];
        for (var i = 0; i < luma.Length; i++)
            luma[i] = (byte)((seed * 31 + i * 7) % 251);

        return luma;
    }

    /// <summary>Oracle for the banded search: all pairs compared, no shortcuts.</summary>
    private static HashSet<string> BruteForceGroups(IReadOnlyList<FileRecord> records, int maxDistance)
    {
        var parent = Enumerable.Range(0, records.Count).ToArray();

        int Find(int i)
        {
            while (parent[i] != i)
                i = parent[i] = parent[parent[i]];

            return i;
        }

        for (var i = 0; i < records.Count; i++)
        for (var j = i + 1; j < records.Count; j++)
        {
            if (PerceptualHash.Distance(records[i].PHash, records[j].PHash) <= maxDistance)
                parent[Find(i)] = Find(j);
        }

        var byRoot = new Dictionary<int, List<string>>();
        for (var i = 0; i < records.Count; i++)
        {
            if (!byRoot.TryGetValue(Find(i), out var members))
                byRoot[Find(i)] = members = [];

            members.Add(records[i].Path);
        }

        return byRoot.Values
            .Where(g => g.Count > 1)
            .Select(g => string.Join('|', g.OrderBy(p => p, StringComparer.Ordinal)))
            .ToHashSet(StringComparer.Ordinal);
    }

    /// <summary>Spreads the flipped bits so they land in different bands, which is what a banded search can miss.</summary>
    private static ulong FlipAcrossBands(ulong hash, int count)
    {
        const int usableBits = 63; // bit 63 is the computed marker and must stay set

        for (var i = 0; i < count; i++)
            hash ^= 1UL << i * usableBits / Math.Max(count, 1);

        return hash;
    }

    private static HashSet<string> Canonical(IReadOnlyList<PerceptualGroup> groups) =>
        groups
            .Select(g => string.Join('|', g.Files.Select(f => f.Path).OrderBy(p => p, StringComparer.Ordinal)))
            .ToHashSet(StringComparer.Ordinal);

    [Theory]
    // Tolerances the UI allows (DuplicateScanService clamps to 1..9), plus the 0 edge.
    [InlineData(0, 0)]
    [InlineData(1, 1)]
    [InlineData(5, 2)]
    [InlineData(9, 3)]
    [InlineData(5, 4)]
    [InlineData(3, 5)]
    public void BandedSearch_MatchesBruteForce(int maxDistance, int seed)
    {
        var rng = new Random(seed);
        var records = new List<FileRecord>();
        var next = 0;

        FileRecord Add(ulong hash) => Rec($"/x/{next++}.jpg", hash);

        for (var i = 0; i < 60; i++)
        {
            var seedHash = (ulong)rng.NextInt64() | (1UL << 63);
            records.Add(Add(seedHash));

            var flips = rng.Next(0, 12);
            var neighbour = seedHash;
            for (var f = 0; f < flips; f++)
                neighbour ^= 1UL << rng.Next(63);

            records.Add(Add(neighbour));

            // At the tolerance (must still be found) and one bit past it (must not be).
            records.Add(Add(FlipAcrossBands(seedHash, maxDistance)));
            records.Add(Add(FlipAcrossBands(seedHash, maxDistance + 1)));

            if (i % 7 == 0)
                records.Add(Add(seedHash));
        }

        FileRecordDatabase.Load(records);
        var expected = BruteForceGroups(records, maxDistance);

        var actual = new PerceptualDuplicateFinder(new FakeThumbnailSource())
            .Scan(maxDistance, ct: TestContext.Current.CancellationToken);

        Assert.Equal(expected, Canonical(actual));
    }

    [Fact]
    public void IdenticalHashes_CollapseIntoOneGroup()
    {
        var records = Enumerable.Range(0, 500)
            .Select(i => Rec($"/x/{i}.jpg", 0xDEAD_BEEF_0000_0001UL))
            .ToList();

        FileRecordDatabase.Load(records);

        var groups = new PerceptualDuplicateFinder(new FakeThumbnailSource())
            .Scan(ct: TestContext.Current.CancellationToken);

        var group = Assert.Single(groups);
        Assert.Equal(records.Count, group.Files.Count);
    }

    [Fact]
    public void HashesEligibleRecords_AndSkipsPsd()
    {
        FileRecordDatabase.Load([Rec("/x/a.jpg"), Rec("/x/b.png"), Rec("/x/c.psd")]);
        var fake = new FakeThumbnailSource();

        new PerceptualDuplicateFinder(fake).Scan(ct: TestContext.Current.CancellationToken);

        var byPath = FileRecordDatabase.Records.ToDictionary(r => r.Path);
        Assert.True(byPath["/x/a.jpg"].HasPHash);
        Assert.True(byPath["/x/b.png"].HasPHash);
        Assert.False(byPath["/x/c.psd"].HasPHash);      // PSD excluded from perceptual hashing
        Assert.DoesNotContain("/x/c.psd", fake.Calls);  // and never even decoded
    }

    [Fact]
    public void SkipsRecordsThatAlreadyHaveAHash()
    {
        FileRecordDatabase.Load([Rec("/x/a.jpg", pHash: 123UL), Rec("/x/b.jpg")]);
        var fake = new FakeThumbnailSource();

        new PerceptualDuplicateFinder(fake).Scan(ct: TestContext.Current.CancellationToken);

        Assert.DoesNotContain("/x/a.jpg", fake.Calls); // already hashed -> not re-decoded
        Assert.Contains("/x/b.jpg", fake.Calls);
    }

    [Fact]
    public void NullLuma_LeavesRecordUnhashed()
    {
        FileRecordDatabase.Load([Rec("/x/a.jpg")]);
        var fake = new FakeThumbnailSource { Provider = _ => null };

        new PerceptualDuplicateFinder(fake).Scan(ct: TestContext.Current.CancellationToken);

        Assert.False(FileRecordDatabase.Records.Single().HasPHash);
    }

    [Fact]
    public void ParallelHashing_AssignsEveryHashToItsOwnFile()
    {
        // Enough files to spread across workers, so a slot/path mix-up would show up here.
        const int count = 200;
        var paths = Enumerable.Range(0, count).Select(i => $"/x/{i}.jpg").ToArray();
        FileRecordDatabase.Load(paths.Select(p => Rec(p)).ToList());

        var expected = paths.ToDictionary(
            p => p,
            p => PerceptualHash.Compute(DistinctLuma(int.Parse(Path.GetFileNameWithoutExtension(p))))
        );

        var fake = new FakeThumbnailSource
        {
            Provider = path => DistinctLuma(int.Parse(Path.GetFileNameWithoutExtension(path)))
        };

        new PerceptualDuplicateFinder(fake).Scan(ct: TestContext.Current.CancellationToken);

        foreach (var record in FileRecordDatabase.Records)
            Assert.Equal(expected[record.Path], record.PHash);

        Assert.Equal(count, fake.Calls.Count); // each file decoded exactly once
    }

    [Fact]
    public void Cancellation_KeepsHashesAlreadyComputed()
    {
        var paths = Enumerable.Range(0, 50).Select(i => $"/x/{i}.jpg").ToArray();
        FileRecordDatabase.Load(paths.Select(p => Rec(p)).ToList());

        using var cts = new CancellationTokenSource();
        var served = 0;
        var fake = new FakeThumbnailSource
        {
            Provider = path =>
            {
                // Cancel partway through, while workers are still in flight.
                if (Interlocked.Increment(ref served) == 5)
                    cts.Cancel();

                return DistinctLuma(int.Parse(Path.GetFileNameWithoutExtension(path)));
            }
        };

        Assert.Throws<OperationCanceledException>(() => new PerceptualDuplicateFinder(fake).Scan(ct: cts.Token));

        // Decode work already paid for is kept. How far the scan got is timing-dependent, so
        // only what was actually served is asserted on.
        var byPath = FileRecordDatabase.Records.ToDictionary(r => r.Path);
        foreach (var path in fake.Calls)
            Assert.True(byPath[path].HasPHash, $"{path} was decoded but its hash was discarded");
    }

    [Fact]
    public void Progress_NeverGoesBackwards_AndEndsAtTotal()
    {
        var paths = Enumerable.Range(0, 100).Select(i => $"/x/{i}.jpg").ToArray();
        FileRecordDatabase.Load(paths.Select(p => Rec(p)).ToList());

        var gate = new Lock();
        var reports = new List<PerceptualScanProgress>();
        var progress = new SynchronousProgress(p =>
        {
            lock (gate)
                reports.Add(p);
        });

        var fake = new FakeThumbnailSource
        {
            Provider = path => DistinctLuma(int.Parse(Path.GetFileNameWithoutExtension(path)))
        };

        new PerceptualDuplicateFinder(fake).Scan(progress: progress, ct: TestContext.Current.CancellationToken);

        Assert.NotEmpty(reports);
        for (var i = 1; i < reports.Count; i++)
        {
            Assert.True(reports[i].FilesHashed >= reports[i - 1].FilesHashed,
                $"progress went backwards: {reports[i - 1].FilesHashed} -> {reports[i].FilesHashed}");
        }

        Assert.All(reports, r => Assert.Equal(paths.Length, r.Total));
        Assert.Equal(paths.Length, reports[^1].FilesHashed);
    }

    /// <summary>Reports inline; <see cref="Progress{T}"/> would post elsewhere and scramble the order under test.</summary>
    private sealed class SynchronousProgress(Action<PerceptualScanProgress> handler) : IProgress<PerceptualScanProgress>
    {
        public void Report(PerceptualScanProgress value) => handler(value);
    }

    [Fact]
    public void Cluster_GroupsNearHashes_AndExcludesFarOnes()
    {
        // a,b get identical (flat) luma -> identical pHash; c gets a gradient -> distance ~63.
        var flat = new byte[PerceptualHash.SampleCount];
        var gradient = new byte[PerceptualHash.SampleCount];
        for (var y = 0; y < PerceptualHash.Height; y++)
        for (var x = 0; x < PerceptualHash.Width; x++)
            gradient[y * PerceptualHash.Width + x] = (byte)(x * 10);

        FileRecordDatabase.Load([Rec("/x/a.jpg"), Rec("/x/b.jpg"), Rec("/x/c.jpg")]);
        var fake = new FakeThumbnailSource
        {
            Provider = path => path.EndsWith("c.jpg") ? gradient : flat
        };

        var groups = new PerceptualDuplicateFinder(fake).Scan(maxDistance: 10, ct: TestContext.Current.CancellationToken);

        var group = Assert.Single(groups);
        var members = group.Files.Select(f => f.Path).OrderBy(p => p, StringComparer.Ordinal).ToArray();
        Assert.Equal(["/x/a.jpg", "/x/b.jpg"], members);
    }
}