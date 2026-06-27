using Lyra.FileLoader.Store;
using Xunit;

namespace Lyra.Core.Tests.Duplicates;

[Collection("FileRecordDatabase")]
public sealed class FileRecordDatabaseDuplicatesTests
{
    private static FileRecord Rec(string path, int? groupId = null) =>
        new(path, Path.GetFileName(path), Path.GetDirectoryName(path) ?? string.Empty, GroupId: groupId);

    [Fact]
    public void EnterDuplicatesView_ShowsOnlyGroupedRecords_OrderedByGroup()
    {
        FileRecordDatabase.Load([
            Rec("/x/a.png", groupId: 2),
            Rec("/x/b.png"),               // ungrouped
            Rec("/x/c.png", groupId: 1),
            Rec("/x/d.png", groupId: 1),
            Rec("/x/e.png", groupId: 2),
        ]);

        Assert.True(FileRecordDatabase.EnterDuplicatesView());
        Assert.True(FileRecordDatabase.InDuplicatesView);

        var shown = FileRecordDatabase.Records.Select(r => r.Path).ToArray();
        // Group 1 (c, d) before group 2 (a, e); ungrouped b excluded.
        Assert.Equal(["/x/c.png", "/x/d.png", "/x/a.png", "/x/e.png"], shown);
    }

    [Fact]
    public void ExitDuplicatesView_RestoresFullCollection()
    {
        FileRecordDatabase.Load([Rec("/x/a.png", groupId: 1), Rec("/x/b.png"), Rec("/x/c.png", groupId: 1)]);

        FileRecordDatabase.EnterDuplicatesView();
        FileRecordDatabase.ExitDuplicatesView();

        Assert.False(FileRecordDatabase.InDuplicatesView);
        Assert.Equal(3, FileRecordDatabase.Count);
    }

    [Fact]
    public void EnterDuplicatesView_NoGroups_ReturnsFalseAndStaysFull()
    {
        FileRecordDatabase.Load([Rec("/x/a.png"), Rec("/x/b.png")]);

        Assert.False(FileRecordDatabase.EnterDuplicatesView());
        Assert.False(FileRecordDatabase.InDuplicatesView);
        Assert.Equal(2, FileRecordDatabase.Count);
    }

    [Fact]
    public void ClearGroups_RemovesAllGroupIds()
    {
        FileRecordDatabase.Load([Rec("/x/a.png", groupId: 1), Rec("/x/b.png", groupId: 1)]);

        FileRecordDatabase.ClearGroups();

        Assert.All(FileRecordDatabase.Records, r => Assert.False(r.HasGroup));
    }
}
