using Lyra.Common;
using Lyra.Imaging.Decoding.Support;
using Xunit;

namespace Lyra.Imaging.Tests.Decoding.Support;

public class ScratchFileCopyLiveShareTests
{
    private const string RealFile = "/mnt/volatile/files/lyra-testfiles/tiff-large/bigtiff-over-4gib.tif";

    [Fact]
    public void CancelsAfterFirstChunk_AgainstRealShare_WithoutWaitingForWholeTransfer()
    {
        Assert.SkipUnless(File.Exists(RealFile), $"Live-share test file not present on this machine: {RealFile}");

        var fileSize = new FileInfo(RealFile).Length;
        Assert.True(fileSize > 100 * DecoderIO.ReadChunk, "File should span many chunks for this test to mean anything");

        using var cts = new CancellationTokenSource();
        var chunksSeenBeforeCancel = 0;
        
        var before = Directory.EnumerateFiles(LyraIO.GetScratchDir(), "tiff-*.tmp").ToHashSet();

        var sw = System.Diagnostics.Stopwatch.StartNew();

        Assert.Throws<OperationCanceledException>(() =>
        {
            ScratchFileCopy.TryCreate(RealFile, fileSize, cts.Token, out _, onProgress: _ =>
            {
                chunksSeenBeforeCancel++;
                // Cancel the moment the first chunk lands - proves the copy loop checks
                // cancellation between chunks rather than only before/after the whole transfer.
                cts.Cancel();
            });
        });

        sw.Stop();

        Assert.Equal(1, chunksSeenBeforeCancel);
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(30), $"Cancellation should abandon the transfer almost immediately after one chunk, took {sw.Elapsed}");

        var leftover = Directory.EnumerateFiles(LyraIO.GetScratchDir(), "tiff-*.tmp")
            .Where(file => !before.Contains(file))
            .ToList();

        Assert.Empty(leftover);
    }

    [Fact]
    public void CopiesRealFileFromSlowShare_ThenCleansUpOnDispose()
    {
        Assert.SkipUnless(File.Exists(RealFile), $"Live-share test file not present on this machine: {RealFile}");

        var fileSize = new FileInfo(RealFile).Length;

        using var cts = new CancellationTokenSource();

        using var scratch = ScratchFileCopy.TryCreate(RealFile, fileSize, cts.Token, out _);

        Assert.NotNull(scratch);
        Assert.True(File.Exists(scratch!.Path));
        Assert.Equal(fileSize, scratch.BytesCopied);
        Assert.Equal(fileSize, new FileInfo(scratch.Path).Length);

        var pathWhileAlive = scratch.Path;

        scratch.Dispose();

        Assert.False(File.Exists(pathWhileAlive), "Scratch copy should be deleted once the caller is done with it");
    }
}
