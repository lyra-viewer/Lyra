using Lyra.Imaging.Decoding.Support;
using Xunit;

namespace Lyra.Imaging.Tests.Decoding.Support;

/// <summary>
/// What the scratch directory keeps and what it throws away.
///
/// It matters more than it used to: scratch copies moved out of the temp directory, which empties
/// itself at reboot and is RAM on half the systems Lyra runs on, into the cache directory, which
/// is real disk and empties itself never. This sweep is now the only thing that clears it - and it
/// has to do that without touching the copies a concurrent run is in the middle of using.
///
/// A run is marked by a file it holds an exclusive handle on, rather than by its process id: two
/// Flatpak instances are both pid 2 in their own namespaces, so each would read the other's
/// leftovers as its own and leave them there forever.
/// </summary>
[Collection(nameof(ScratchSession))]
public class ScratchSweepTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"lyra-sweep-{Guid.NewGuid():N}");

    public ScratchSweepTests() => Directory.CreateDirectory(_dir);

    private string Mark(string token) => Path.Combine(_dir, $"{token}.lock");

    private string Copy(string token) => Path.Combine(_dir, $"{token}-{Guid.NewGuid():N}.tmp");

    private string WriteMark(string token)
    {
        var path = Mark(token);
        File.WriteAllText(path, string.Empty);

        return path;
    }

    private string WriteCopy(string token)
    {
        var path = Copy(token);
        File.WriteAllText(path, "pixels");

        return path;
    }

    /// <summary>Holds a run's mark the way a running instance does.</summary>
    private FileStream HoldMark(string token) => new(Mark(token), FileMode.CreateNew, FileAccess.Write, FileShare.None);

    [Fact]
    public void AFinishedRunsCopiesAndMarkAreBothCleared()
    {
        var mark = WriteMark("aaaa");
        var copy = WriteCopy("aaaa");

        ScratchFileCopy.Sweep(_dir);

        Assert.False(File.Exists(copy));
        Assert.False(File.Exists(mark));
    }

    [Fact]
    public void ARunningInstancesCopiesAreLeftAlone()
    {
        using var held = HoldMark("bbbb");
        var copy = WriteCopy("bbbb");

        ScratchFileCopy.Sweep(_dir);

        // Deleting these would fail that instance's decode: the copy is opened again by path once
        // the copy stream closes, so it has to survive as long as the run that made it.
        Assert.True(File.Exists(copy));
        Assert.True(File.Exists(Mark("bbbb")));
    }

    [Fact]
    public void ACopyWithNoMarkBesideItIsCleared()
    {
        // A mark is written before the first copy, so this cannot be a run that is only starting:
        // it is one whose mark has already gone.
        var orphan = WriteCopy("cccc");

        ScratchFileCopy.Sweep(_dir);

        Assert.False(File.Exists(orphan));
    }

    [Fact]
    public void OneRunsLeftoversGoWhileAnothersStay()
    {
        using var held = HoldMark("live");

        var liveCopies = new[] { WriteCopy("live"), WriteCopy("live") };
        var deadMark = WriteMark("dead");
        var deadCopies = new[] { WriteCopy("dead"), WriteCopy("dead") };

        ScratchFileCopy.Sweep(_dir);

        Assert.All(liveCopies, copy => Assert.True(File.Exists(copy)));
        Assert.All(deadCopies, copy => Assert.False(File.Exists(copy)));
        Assert.False(File.Exists(deadMark));
    }

    [Fact]
    public void SweepingAnEmptyOrMissingDirectoryIsHarmless()
    {
        ScratchFileCopy.Sweep(_dir);
        ScratchFileCopy.Sweep(Path.Combine(_dir, "not-there"));

        Assert.Empty(Directory.EnumerateFiles(_dir));
    }

    [Fact]
    public void AHeldMarkReadsAsRunning_AndAReleasedOneAsFinished()
    {
        var path = Mark("dddd");

        using (new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        {
            Assert.False(ScratchSession.HasEnded(path));
        }

        Assert.True(ScratchSession.HasEnded(path));
        Assert.True(ScratchSession.HasEnded(Mark("never-existed")));
    }

    [Fact]
    public void AClaimedRunIsMarkedAndIsNotSweptAway()
    {
        var token = ScratchSession.Claim(_dir);

        Assert.NotNull(token);
        Assert.True(File.Exists(Mark(token!)), "claiming a run should leave the mark that proves it is running");

        var copy = WriteCopy(token);
        ScratchFileCopy.Sweep(_dir);

        Assert.True(File.Exists(copy));
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_dir, recursive: true);
        }
        catch (IOException)
        {
            // This run's own mark is still held - it is released when the process exits, and a
            // leftover temp directory is not worth failing a test over.
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
