using Lyra.Common;
using Lyra.Imaging.Decoding.Support;
using Lyra.Imaging.Tests.Support;
using Xunit;

namespace Lyra.Imaging.Tests.Decoding.Support;

/// <summary>
/// The copy itself, through the real scratch directory this machine would use. Reachable in
/// production only for files too large to buffer - above a quarter of installed memory - which is
/// why it is worth exercising here rather than waiting for a machine small enough to reach it.
/// </summary>
[Collection(nameof(ScratchSession))]
public class ScratchFileCopyTests : IDisposable
{
    private readonly string _source = Path.Combine(Path.GetTempPath(), $"lyra-source-{Guid.NewGuid():N}.bin");

    public ScratchFileCopyTests() => File.WriteAllBytes(_source, TempFile.Pattern(3 * DecoderIO.ReadChunk + 17));

    [Fact]
    public void CopiesTheWholeFile_MarksTheRunThatOwnsIt_AndClearsUpAfterwards()
    {
        var size = new FileInfo(_source).Length;

        string path;
        using (var scratch = ScratchFileCopy.TryCreate(_source, size, CancellationToken.None, out var elapsedMs))
        {
            Assert.NotNull(scratch);
            path = scratch!.Path;

            Assert.Equal(size, scratch.BytesCopied);
            Assert.Equal(File.ReadAllBytes(_source), File.ReadAllBytes(path));
            Assert.True(elapsedMs >= 0);
            
            var token = ScratchSession.CurrentToken;
            
            Assert.NotNull(token);
            Assert.StartsWith(token!, Path.GetFileName(path), StringComparison.Ordinal);
            Assert.True(File.Exists(Path.Combine(LyraIO.GetScratchDir(), token + ScratchSession.LockSuffix)));
            
            ScratchFileCopy.Sweep(LyraIO.GetScratchDir());
            Assert.True(File.Exists(path));
        }

        Assert.False(File.Exists(path));
    }

    [Fact]
    public void CancellingAfterTheFirstChunk_StopsThereAndLeavesNothingBehind()
    {
        var size = new FileInfo(_source).Length;
        var before = Directory.EnumerateFiles(LyraIO.GetScratchDir(), "*.tmp").ToHashSet();

        using var cts = new CancellationTokenSource();
        var chunksSeen = 0;

        Assert.Throws<OperationCanceledException>(() =>
            ScratchFileCopy.TryCreate(_source, size, cts.Token, out _, onProgress: _ =>
            {
                chunksSeen++;
                cts.Cancel();
            }));

        // Checked between chunks, not only before and after the whole transfer.
        Assert.Equal(1, chunksSeen);

        var leftover = Directory.EnumerateFiles(LyraIO.GetScratchDir(), "*.tmp").Where(file => !before.Contains(file));
        Assert.Empty(leftover);
    }

    [Fact]
    public void RefusesAnEmptyFileRatherThanMakingAnEmptyCopy()
    {
        Assert.Null(ScratchFileCopy.TryCreate(_source, 0, CancellationToken.None, out _));
    }

    public void Dispose()
    {
        try
        {
            File.Delete(_source);
        }
        catch (IOException) { }
    }
}