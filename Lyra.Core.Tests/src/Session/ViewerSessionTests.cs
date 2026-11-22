using Lyra.FileLoader.Duplicates;
using Lyra.FileLoader.Enumeration;
using Lyra.FileLoader.Store;
using Lyra.Common;
using Lyra.GUI;
using Lyra.Imaging;
using Lyra.Imaging.Content;
using Lyra.Session;
using Xunit;

namespace Lyra.Core.Tests.Session;

public class ViewerSessionTests
{
    [Fact]
    public void TheNewImageOpens_BeforeTheRestIsLetGo()
    {
        var cache = new RecordingCache();
        var session = SessionOver(cache, "/x/a.png", "/x/b.png");

        session.OpenCurrent();

        Assert.Equal(["open /x/a.png", "cleanup /x/a.png,/x/b.png", "preload /x/a.png,/x/b.png"], cache.Calls);
    }

    [Fact]
    public void MissingFiles_ArePurged_BeforeOpening()
    {
        var cache = new RecordingCache();
        var session = SessionOver(cache, ["/x/a.png", "/x/b.png"], exists: path => path != "/x/a.png");

        var composite = session.OpenCurrent();

        Assert.Equal("/x/b.png", composite?.FileInfo.FullName);
        Assert.Equal(["purge /x/a.png", "open /x/b.png", "cleanup /x/b.png", "preload /x/b.png"], cache.Calls);
        Assert.Equal(1, session.Navigator.Store.Count);
    }

    [Fact]
    public void AnEmptyCollection_ClosesTheImageOnScreen()
    {
        var cache = new RecordingCache();
        var session = SessionOver(cache, ["/x/a.png"], exists: _ => false);

        Assert.Null(session.OpenCurrent());
        Assert.Equal(["purge /x/a.png", "close", "cleanup "], cache.Calls);
    }

    [Fact]
    public void AScanOfAnEarlierCollection_IsNotApplied()
    {
        var session = SessionOver(new RecordingCache(), "/x/a.png", "/x/b.png");
        var store = session.Navigator.Store;
        var result = new DuplicateScanResult(Grouped(store.Records), store.Generation, GroupCount: 1);

        session.ApplyCollection(Collection("/x/a.png", "/x/b.png"));

        Assert.False(session.ApplyScan(result, finished: true));
        Assert.All(session.Navigator.Store.Records, r => Assert.False(r.HasGroup));
    }

    [Fact]
    public void AFinishedScan_IsApplied_AndTheDuplicatesViewOpens()
    {
        var session = SessionOver(new RecordingCache(), "/x/a.png", "/x/b.png", "/x/c.png");
        var store = session.Navigator.Store;
        var result = new DuplicateScanResult(Grouped(store.Records.Take(2)), store.Generation, GroupCount: 1);

        Assert.True(session.ApplyScan(result, finished: true));
        Assert.True(session.TryEnterDuplicatesView());

        Assert.Equal(ViewMode.Duplicates, session.ViewMode);
        Assert.Equal(2, session.Navigator.Store.Count);

        Assert.True(session.TryLeaveViewMode());
        Assert.Equal(3, session.Navigator.Store.Count);
    }

    [Fact]
    public void ACancelledScan_KeepsWhatItLearned_ButNoGroups()
    {
        var session = SessionOver(new RecordingCache(), "/x/a.png", "/x/b.png");
        var store = session.Navigator.Store;
        var learned = store.Records.Select(r => r with { PHash = 11, GroupId = 1 }).ToArray();

        Assert.False(session.ApplyScan(new DuplicateScanResult(learned, store.Generation, GroupCount: 0), finished: false));

        Assert.All(session.Navigator.Store.Records, r =>
        {
            Assert.Equal(11UL, r.PHash);
            Assert.False(r.HasGroup);
        });
    }

    [Fact]
    public void TheCollectionsDecoders_AreWarmed_BehindTheFirstImageOpened()
    {
        var cache = new RecordingCache();
        var session = SessionOver(cache, "/x/a.png", "/x/b.svg", "/x/c.heic");

        Assert.Empty(cache.Calls);

        session.OpenCurrent();
        session.OpenCurrent();

        Assert.Equal(["open /x/a.png", "cleanup /x/a.png,/x/b.svg,/x/c.heic", "preload /x/a.png,/x/b.svg,/x/c.heic", $"prewarm {WarmTarget.Svg},{WarmTarget.Heic}"], cache.Calls.Take(4));
        Assert.Single(cache.Calls, call => call.StartsWith("prewarm"));
    }

    [Fact]
    public void ANewCollection_IsWarmedForWhatItHolds()
    {
        var cache = new RecordingCache();
        var session = SessionOver(cache, "/x/a.svg");
        session.OpenCurrent();

        session.ApplyCollection(Collection("/x/b.exr"));
        session.OpenCurrent();

        Assert.Equal([$"prewarm {WarmTarget.Svg}", $"prewarm {WarmTarget.Exr}"], cache.Calls.Where(call => call.StartsWith("prewarm")));
    }

    [Fact]
    public void ACollectionWithNothingToWarm_AsksForNothing()
    {
        var cache = new RecordingCache();
        SessionOver(cache, "/x/a.png", "/x/b.jpg").OpenCurrent();

        Assert.DoesNotContain(cache.Calls, call => call.StartsWith("prewarm"));
    }

    [Fact]
    public void WarmTargets_FollowTheContents_ThenTheExtension()
    {
        FileRecord[] files =
        [
            new("/x/a.AVIF", "a.AVIF", "/x"),
            new("/x/b.avif", "b.avif", "/x"),
            new("/x/c.ktx", "c.ktx", "/x"),
            new("/x/d.png", "d.png", "/x", Detected: ImageFormatType.Svg),
            new("/x/e", "e", "/x")
        ];

        Assert.Equal([WarmTarget.Svg, WarmTarget.Avif], ViewerSession.WarmTargetsIn(files).Order());
    }

    private static ViewerSession SessionOver(RecordingCache cache, params string[] paths) =>
        SessionOver(cache, paths, exists: _ => true);

    private static ViewerSession SessionOver(RecordingCache cache, string[] paths, Func<string, bool> exists)
    {
        var session = new ViewerSession(cache, exists);
        session.ApplyCollection(Collection(paths));
        return session;
    }

    private static CollectionLoadResult Collection(params string[] paths) =>
        new(
            paths.Select(p => new FileRecord(p, Path.GetFileName(p), Path.GetDirectoryName(p)!)).ToList(),
            ["/x"],
            SingleDirectory: true,
            TopDirectory: "/x",
            new FileDropContext([], [], [], IsSameDirectoryGroup: true, AnchorPath: null, IsSingleFileOpen: false),
            CollectionType.SingleDirectoryCollection
        );

    private static FileRecord[] Grouped(IEnumerable<FileRecord> records) => records.Select(r => r with { GroupId = 1 }).ToArray();

    private sealed class RecordingCache : IImageCache
    {
        public List<string> Calls { get; } = [];

        public Composite Open(string path)
        {
            Calls.Add($"open {path}");
            return new Composite(new FileInfo(path));
        }

        public void Close() => Calls.Add("close");

        public bool IsLoading(string path) => false;

        public void Preload(string[] paths) => Calls.Add($"preload {string.Join(',', paths)}");

        public void Cleanup(string[] keep) => Calls.Add($"cleanup {string.Join(',', keep)}");

        public void Purge(string path) => Calls.Add($"purge {path}");

        public void Prewarm(IReadOnlySet<WarmTarget> targets) => Calls.Add($"prewarm {string.Join(',', targets.Order())}");
    }
}