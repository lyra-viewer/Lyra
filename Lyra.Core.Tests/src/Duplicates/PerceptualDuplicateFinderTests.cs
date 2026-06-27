using Lyra.FileLoader.Duplicates.Perceptual;
using Lyra.FileLoader.Store;
using Xunit;

namespace Lyra.Core.Tests.Duplicates;

[Collection("FileRecordDatabase")]
public sealed class PerceptualDuplicateFinderTests
{
    private sealed class FakeThumbnailSource : IThumbnailSource
    {
        public readonly List<string> Calls = [];
        public Func<string, byte[]?> Provider = _ => new byte[PerceptualHash.SampleCount];

        public byte[]? GetLuma(string path, int width, int height, CancellationToken ct = default)
        {
            Calls.Add(path);
            return Provider(path);
        }
    }

    private static FileRecord Rec(string path, ulong pHash = 0) =>
        new(path, Path.GetFileName(path), Path.GetDirectoryName(path) ?? string.Empty, PHash: pHash);

    [Fact]
    public void HashesEligibleRecords_AndSkipsPsd()
    {
        FileRecordDatabase.Load([Rec("/x/a.jpg"), Rec("/x/b.png"), Rec("/x/c.psd")]);
        var fake = new FakeThumbnailSource();

        new PerceptualDuplicateFinder(fake).Scan(ct: TestContext.Current.CancellationToken);

        var byPath = FileRecordDatabase.Records.ToDictionary(r => r.Path);
        Assert.True(byPath["/x/a.jpg"].HasPHash);
        Assert.True(byPath["/x/b.png"].HasPHash);
        Assert.False(byPath["/x/c.psd"].HasPHash); // PSD excluded from perceptual hashing
        Assert.DoesNotContain("/x/c.psd", fake.Calls); // and never even decoded
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