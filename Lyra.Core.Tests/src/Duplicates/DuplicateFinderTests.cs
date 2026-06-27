using Lyra.FileLoader.Duplicates.Exact;
using Lyra.FileLoader.Store;
using Xunit;

namespace Lyra.Core.Tests.Duplicates;

[Collection("FileRecordDatabase")]
public sealed class DuplicateFinderTests : IDisposable
{
    private readonly string _root;

    public DuplicateFinderTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "Lyra_DuplicateFinderTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch
        {
            /* ignore cleanup errors */
        }
    }

    private string Write(string name, byte[] content)
    {
        var path = Path.Combine(_root, name);
        File.WriteAllBytes(path, content);
        return path;
    }

    private static FileRecord Rec(string path) =>
        new(path, Path.GetFileName(path), Path.GetDirectoryName(path) ?? string.Empty);

    [Fact]
    public void FindsExactDuplicates_AndIgnoresSameSizeDifferentContentAndUniqueSizes()
    {
        var dupA = Write("a.bin", [1, 2, 3, 4, 5]);
        var dupB = Write("b.bin", [1, 2, 3, 4, 5]); // identical to dupA
        Write("c.bin", [9, 9, 9, 9, 9]); // same size (5), different content
        Write("d.bin", [7]); // unique size

        FileRecordDatabase.Load([Rec(dupA), Rec(dupB), Rec(Path.Combine(_root, "c.bin")), Rec(Path.Combine(_root, "d.bin"))]);

        var groups = DuplicateFinder.Scan(ct: TestContext.Current.CancellationToken);

        var group = Assert.Single(groups);
        var paths = group.Files.Select(f => f.Path).OrderBy(p => p, StringComparer.Ordinal).ToArray();
        Assert.Equal(new[] { dupA, dupB }.OrderBy(p => p, StringComparer.Ordinal).ToArray(), paths);
    }

    [Fact]
    public void IgnoresZeroByteFiles_EvenWhenIdentical()
    {
        FileRecordDatabase.Load([Rec(Write("z1.bin", [])), Rec(Write("z2.bin", []))]);

        Assert.Empty(DuplicateFinder.Scan(ct: TestContext.Current.CancellationToken));
    }

    [Fact]
    public void PopulatesSizeLazilyDuringScan()
    {
        var path = Write("solo.bin", [1, 2, 3]);
        FileRecordDatabase.Load([Rec(path)]); // Size starts at 0 (unknown)

        DuplicateFinder.Scan(ct: TestContext.Current.CancellationToken);

        Assert.Equal(3, FileRecordDatabase.Records.Single().Size);
    }
}