using Lyra.Imaging.Decoding.Support;
using Lyra.Imaging.Tests.Support;
using Xunit;

namespace Lyra.Imaging.Tests.Decoding;

/// <summary>
/// The chunked whole-file read the decoders use in place of <c>File.ReadAllBytes</c>. It exists for
/// two properties: it observes cancellation, so a large file on a stalled mount cannot pin its decode
/// task after the user has navigated away, and it reports bytes as they arrive, which is what separates
/// transfer time from decode time. Both only hold if the bytes still come back exactly right, so that
/// is what most of these covers.
/// </summary>
public class DecoderReadTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(64 * 1024)]
    [InlineData(1024 * 1024)]
    [InlineData(1024 * 1024 + 1)]
    [InlineData(2 * 1024 * 1024)]
    [InlineData(3 * 1024 * 1024 + 7777)]
    public void ReadsBackExactlyWhatWasWritten(int size)
    {
        using var file = new TempFile(TempFile.Pattern(size));

        var read = DecoderIO.ReadAllBytes(file.Path, CancellationToken.None, out _);

        Assert.Equal(size, read.Length);
        Assert.True(read.AsSpan().SequenceEqual(TempFile.Pattern(size)));
    }

    [Fact]
    public void EmptyFileReadsAsAnEmptyArray()
    {
        using var file = new TempFile([]);

        Assert.Empty(DecoderIO.ReadAllBytes(file.Path, CancellationToken.None, out _));
    }

    [Fact]
    public void ProgressReportsAMonotonicRunningTotalEndingAtTheFileLength()
    {
        const int size = 3 * 1024 * 1024 + 512;
        using var file = new TempFile(TempFile.Pattern(size));

        var reports = new List<long>();
        DecoderIO.ReadAllBytes(file.Path, CancellationToken.None, out _, reports.Add);

        Assert.NotEmpty(reports);
        Assert.Equal(size, reports[^1]);
        Assert.Equal(reports.Order().ToList(), reports);
        Assert.All(reports, r => Assert.InRange(r, 1, size));
    }

    [Fact]
    public void TheReadReportsItsOwnDuration()
    {
        using var file = new TempFile(TempFile.Pattern(2 * 1024 * 1024));

        DecoderIO.ReadAllBytes(file.Path, CancellationToken.None, out var elapsedMs);

        Assert.True(elapsedMs >= 0);
        Assert.True(elapsedMs < 60_000, $"a local 2 MB read reported {elapsedMs} ms");
    }

    [Fact]
    public void AlreadyCancelledTokenThrowsBeforeReadingAnything()
    {
        using var file = new TempFile(TempFile.Pattern(4096));
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var reported = false;
        Assert.ThrowsAny<OperationCanceledException>(() => DecoderIO.ReadAllBytes(file.Path, cts.Token, out _, _ => reported = true));

        Assert.False(reported);
    }

    [Fact]
    public void CancellingPartwayThroughAbandonsTheRemainingChunks()
    {
        using var file = new TempFile(TempFile.Pattern(8 * 1024 * 1024));
        using var cts = new CancellationTokenSource();

        var chunks = 0;
        Assert.ThrowsAny<OperationCanceledException>(() => DecoderIO.ReadAllBytes(
            file.Path,
            cts.Token,
            out _,
            _ =>
            {
                chunks++;
                cts.Cancel();
            }));

        // One chunk in flight when cancellation was requested, and no chunk after it.
        Assert.Equal(1, chunks);
    }
}