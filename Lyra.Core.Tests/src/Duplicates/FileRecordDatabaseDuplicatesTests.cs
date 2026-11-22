using Lyra.FileLoader.Store;
using Xunit;

namespace Lyra.Core.Tests.Duplicates;

public sealed class FileRecordDatabaseDuplicatesTests
{
    private readonly FileRecordDatabase _db = new();

    private static FileRecord Rec(string path, int? groupId = null) =>
        new(path, Path.GetFileName(path), Path.GetDirectoryName(path) ?? string.Empty, GroupId: groupId);

    [Fact]
    public void EnterDuplicatesView_ShowsOnlyGroupedRecords_OrderedByGroup()
    {
        _db.Load([
            Rec("/x/a.png", groupId: 2),
            Rec("/x/b.png"),               // ungrouped
            Rec("/x/c.png", groupId: 1),
            Rec("/x/d.png", groupId: 1),
            Rec("/x/e.png", groupId: 2),
        ]);

        Assert.True(_db.EnterDuplicatesView());
        Assert.True(_db.InDuplicatesView);

        var shown = _db.Records.Select(r => r.Path).ToArray();
        // Group 1 (c, d) before group 2 (a, e); ungrouped b excluded.
        Assert.Equal(["/x/c.png", "/x/d.png", "/x/a.png", "/x/e.png"], shown);
    }

    [Fact]
    public void ExitDuplicatesView_RestoresFullCollection()
    {
        _db.Load([Rec("/x/a.png", groupId: 1), Rec("/x/b.png"), Rec("/x/c.png", groupId: 1)]);

        _db.EnterDuplicatesView();
        _db.ExitDuplicatesView();

        Assert.False(_db.InDuplicatesView);
        Assert.Equal(3, _db.Count);
    }

    [Fact]
    public void EnterDuplicatesView_NoGroups_ReturnsFalseAndStaysFull()
    {
        _db.Load([Rec("/x/a.png"), Rec("/x/b.png")]);

        Assert.False(_db.EnterDuplicatesView());
        Assert.False(_db.InDuplicatesView);
        Assert.Equal(2, _db.Count);
    }

    [Fact]
    public void Remove_InDuplicatesView_AlsoRemovesFromFullCollection()
    {
        _db.Load([Rec("/x/a.png", groupId: 1), Rec("/x/b.png"), Rec("/x/c.png", groupId: 1)]);
        _db.EnterDuplicatesView();

        Assert.True(_db.Remove("/x/a.png"));
        _db.ExitDuplicatesView();

        Assert.Equal(["/x/b.png", "/x/c.png"], _db.Records.Select(r => r.Path).ToArray());
    }

    [Fact]
    public void ExitDuplicatesView_LandsOnShownDuplicate()
    {
        _db.Load([Rec("/x/a.png", groupId: 1), Rec("/x/b.png"), Rec("/x/c.png", groupId: 1)]);
        _db.MoveTo("/x/b.png");
        _db.EnterDuplicatesView();
        _db.MoveTo("/x/c.png");

        _db.ExitDuplicatesView();

        Assert.Equal("/x/c.png", _db.Current?.Path);
    }

    [Fact]
    public void ExitDuplicatesView_EmptySubset_FallsBackToPreScanRecord()
    {
        _db.Load([Rec("/x/a.png", groupId: 1), Rec("/x/b.png"), Rec("/x/c.png")]);
        _db.MoveTo("/x/c.png");
        _db.EnterDuplicatesView();

        _db.Remove("/x/a.png");
        _db.ExitDuplicatesView();

        Assert.Equal("/x/c.png", _db.Current?.Path);
    }

    [Fact]
    public void Remove_LastDuplicate_LeavesSubsetEmptyAndFullCollectionIntact()
    {
        _db.Load([Rec("/x/a.png", groupId: 1), Rec("/x/b.png")]);
        _db.EnterDuplicatesView();

        _db.Remove("/x/a.png");

        Assert.True(_db.InDuplicatesView);
        Assert.Null(_db.Current);

        _db.ExitDuplicatesView();
        Assert.Equal(["/x/b.png"], _db.Records.Select(r => r.Path).ToArray());
    }

    [Fact]
    public void ApplyScan_WithGroups_ReplacesEarlierGrouping_AndKeepsWhatWasLearned()
    {
        _db.Load([Rec("/x/a.png", groupId: 1), Rec("/x/b.png", groupId: 1), Rec("/x/c.png")]);

        _db.ApplyScan([Rec("/x/a.png") with { PHash = 7 }, Rec("/x/c.png", groupId: 3) with { Size = 42 }], groups: true);

        var byPath = _db.Records.ToDictionary(r => r.Path);
        Assert.Null(byPath["/x/a.png"].GroupId);
        Assert.Equal(7UL, byPath["/x/a.png"].PHash);
        Assert.Null(byPath["/x/b.png"].GroupId);
        Assert.Equal(3, byPath["/x/c.png"].GroupId);
        Assert.Equal(42, byPath["/x/c.png"].Size);
    }

    [Fact]
    public void ApplyScan_WithoutGroups_KeepsTheGroups_ButTakesTheHashes()
    {
        _db.Load([Rec("/x/a.png", groupId: 1) with { PHash = 5 }]);

        _db.ApplyScan([Rec("/x/a.png") with { ContentHash = 9 }], groups: false);

        var record = _db.Records.Single();
        Assert.Equal(1, record.GroupId);
        Assert.Equal(5UL, record.PHash);
        Assert.Equal((UInt128)9, record.ContentHash);
    }

    [Fact]
    public void ApplyScan_SkipsRecordsRemovedMeanwhile()
    {
        _db.Load([Rec("/x/a.png"), Rec("/x/b.png")]);
        _db.Remove("/x/a.png");

        _db.ApplyScan([Rec("/x/a.png", groupId: 1), Rec("/x/b.png", groupId: 1)], groups: true);

        Assert.Equal(["/x/b.png"], _db.Records.Select(r => r.Path).ToArray());
        Assert.Equal(1, _db.Records.Single().GroupId);
    }

    [Fact]
    public void Load_ChangesTheGeneration_ButRemoveDoesNot()
    {
        _db.Load([Rec("/x/a.png"), Rec("/x/b.png")]);
        var first = _db.Generation;

        _db.Remove("/x/a.png");
        Assert.Equal(first, _db.Generation);

        _db.Load([Rec("/x/c.png")]);
        Assert.NotEqual(first, _db.Generation);
    }
}
