using Lyra.Common;
using Lyra.Common.Settings.Enums;
using Lyra.Imaging.Content;
using Lyra.Imaging.Content.Status;
using Lyra.Imaging.Decoding;
using Lyra.Imaging.Decoding.Decoders;
using Lyra.Imaging.Decoding.Support;
using Lyra.Imaging.Detection;
using Lyra.Imaging.Tests.Support;
using Xunit;

namespace Lyra.Imaging.Tests.Decoding;

/// <summary>What a decoder is handed: where its bytes come from, and the settings it runs under.</summary>
public class DecodeContextTests
{
    private const string Nowhere = "/nowhere/inside-a-model.tif";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public void BytesHeldInMemory_AreReadAsTheyAre_AndCountedAsTransferred()
    {
        byte[] bytes = [1, 2, 3, 4];
        using var composite = new Composite(new FileInfo(Nowhere), ImageFormatType.Png, bytes);
        composite.BeginLoadTiming();

        var context = DecodeContext.For(composite);

        Assert.True(context.Source.InMemory);
        Assert.Same(bytes, context.ReadAllBytes(Ct));
        Assert.Equal(bytes.Length, composite.Timing.TransferBytesRead);
    }

    [Fact]
    public void AFileOnlyFormat_RefusesBytesHeldInMemory_WithoutOpeningAnyPath()
    {
        using var composite = new Composite(new FileInfo(Nowhere), ImageFormatType.Tiff, [0x49, 0x49, 0x2A, 0x00]);

        var thrown = Assert.Throws<LoadFailureException>(() => new TiffDecoder().DecodeAsync(composite, Ct).GetAwaiter().GetResult());

        Assert.Contains("held inside another file", thrown.Message);
        Assert.True(LoadFailure.From(thrown).IsExpected);
    }

    [Fact]
    public void AFileSource_ReadsTheFile()
    {
        using var file = new TempFile([9, 8, 7]);
        var source = DecodeSource.File(file.Path, 3);

        using var stream = source.OpenSequential();
        Assert.Equal(9, stream.ReadByte());
        Assert.Equal(file.Path, source.RequireFile());
    }

    [Fact]
    public void TheContextKeepsTheSettingsItWasGiven()
    {
        using var file = new TempFile([0]);
        using var composite = new Composite(new FileInfo(file.Path));
        var options = new DecodeOptions(320, 200, new ToneCurve(ToneMapMode.ReinhardExtended, 4f));

        var context = new DecodeContext(composite, DecodeSource.File(file.Path, 1), options);

        Assert.Same(options, context.Options);
        Assert.Equal((320, 100), context.Options.PreviewSize(6400, 2000));
    }

    [Fact]
    public void TheSettingsSnapshot_IsTheCurveInForceWhenTheDecodeStarts()
    {
        Assert.Equal(HdrDecodeSettings.Curve, DecodeOptions.Current().Curve);
    }

    [Fact]
    public void TheHeadDetectionRead_IsUsedWithoutOpeningTheFileAgain()
    {
        byte[] head = [0x49, 0x49, 0x2B, 0x00, 8, 0, 0, 0];
        var source = DecodeSource.File(Nowhere, 1024, head);

        Assert.Equal(head, source.Head.ToArray());
    }

    [Fact]
    public void WithoutAHeadFromDetection_TheFileIsReadOnceForIt()
    {
        var bytes = Enumerable.Range(0, 100).Select(i => (byte)i).ToArray();
        using var file = new TempFile(bytes);
        var source = DecodeSource.File(file.Path, bytes.Length);

        Assert.Equal(bytes[..64], source.Head.ToArray());

        File.Delete(file.Path);
        Assert.Equal(bytes[..64], source.Head.ToArray());
    }

    [Fact]
    public void TheHeadOfAShortSource_IsAllOfIt()
    {
        Assert.Equal(3, DecodeSource.Memory("tiny", [1, 2, 3]).Head.Length);
    }

    [Fact]
    public void DetectionHandsOnTheBytesItRead()
    {
        var bytes = Enumerable.Range(0, 100).Select(i => (byte)(i + 1)).ToArray();
        using var file = new TempFile(bytes);

        FormatDetector.Detect(file.Path, out var head);

        Assert.Equal(bytes[..64], head);
    }

    [Theory]
    [InlineData(8192, 8192, 4, true)]
    [InlineData(8193, 8192, 4, false)]
    [InlineData(16384, 16384, 1, true)]
    [InlineData(4_000_000_000L, 4_000_000_000L, 4, false)]
    [InlineData(0, 0, 4, true)]
    public void OneRuleSaysWhatFitsInOneTexture(long width, long height, int bytesPerPixel, bool fits)
    {
        Assert.Equal(fits, DecodePolicy.FitsOneTexture(width, height, bytesPerPixel));
    }
}
