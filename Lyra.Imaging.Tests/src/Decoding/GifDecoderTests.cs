using Lyra.Common;
using Lyra.Imaging.Content;
using Lyra.Imaging.Decoding.Decoders;
using Lyra.Imaging.Decoding.Decoders.Gif;
using Lyra.Imaging.Decoding.Structure;
using Lyra.Imaging.Loading;
using Lyra.Imaging.Tests.Support;
using SkiaSharp;
using Xunit;

namespace Lyra.Imaging.Tests.Decoding;

public class GifDecoderTests
{
    private const byte Red = 1, Green = 2, Blue = 3;

    private const uint OpaqueRed = 0xFFFF0000, OpaqueGreen = 0xFF00FF00, OpaqueBlue = 0xFF0000FF, Clear = 0;

    private const int Width = 16, Height = 12;

    [Fact]
    public void GifIsRegistered()
    {
        Assert.Equal(ImageFormatType.Gif, ImageFormat.GetImageFormat(".gif"));
        Assert.IsType<GifDecoder>(DecoderManager.GetDecoder(ImageFormatType.Gif));
    }

    [Fact]
    public void BlockReader_ReadsWhatSkiaDoesNotReport()
    {
        var builder = new GifBuilder(Width, Height) { LoopCount = 3 };
        builder.Comments.Add("made by a test");

        var gif = builder
            .AddSolid(Red)
            .Add(new GifBuilder.Frame(2, 3, 4, 5, (_, _) => 1) { Interlaced = true, LocalPalette = [(1, 2, 3), (4, 5, 6)] })
            .Add(new GifBuilder.Frame(0, 0, 2, 2, (_, _) => 0) { TransparentIndex = 0 })
            .Build();

        var blocks = GifBlockReader.Read(gif);

        Assert.NotNull(blocks);
        Assert.Equal("GIF89a", blocks.Version);
        Assert.Equal(4, blocks.GlobalPaletteSize);
        Assert.Equal(3, blocks.LoopCount);
        Assert.Equal(1, blocks.CommentCount);
        Assert.False(blocks.Truncated);

        Assert.Equal(3, blocks.Frames.Count);
        Assert.True(blocks.Frames[1].Interlaced);
        Assert.Equal(2, blocks.Frames[1].LocalPaletteSize);
        Assert.False(blocks.Frames[0].Transparent);
        Assert.True(blocks.Frames[2].Transparent);

        Assert.All(blocks.Frames, frame => Assert.True(frame.EncodedBytes > 10));
        Assert.True(blocks.Frames.Sum(f => f.EncodedBytes) < gif.Length);
    }

    [Fact]
    public void BlockReader_LeavesATextBlocksTransparencyToTheText()
    {
        var gif = new GifBuilder(Width, Height)
            .AddRaw(0x21, 0xF9, 4, 0x01, 0, 0, 0, 0)
            .AddRaw([0x21, 0x01, 12, .. new byte[12], 2, (byte)'h', (byte)'i', 0])
            .Add(new GifBuilder.Frame(0, 0, Width, Height, (_, _) => Red) { HasControl = false })
            .Build();

        Assert.False(Assert.Single(GifBlockReader.Read(gif)!.Frames).Transparent);
    }

    [Fact]
    public void BlockReader_RejectsWhatIsNotAGif()
    {
        Assert.Null(GifBlockReader.Read("not a gif at all"u8));
        Assert.Null(GifBlockReader.Read("GIF89a"u8));
        Assert.Null(GifBlockReader.Read([]));
    }

    [Fact]
    public void BlockReader_StopsAtTheCut_KeepingTheWholeFrames()
    {
        var gif = new GifBuilder(Width, Height).AddSolid(Red).AddSolid(Green).AddSolid(Blue).Build();
        var whole = GifBlockReader.Read(gif)!;

        var cut = gif.AsSpan(0, gif.Length - (int)whole.Frames[2].EncodedBytes / 2);
        var blocks = GifBlockReader.Read(cut);

        Assert.NotNull(blocks);
        Assert.True(blocks.Truncated);
        Assert.Equal(2, blocks.Frames.Count);
    }

    [Fact]
    public void AnAnimation_IsPublishedAsFrames()
    {
        var gif = new GifBuilder(Width, Height) { LoopCount = 0 }
            .AddSolid(Red, delayCentiseconds: 10)
            .Add(new GifBuilder.Frame(2, 3, 4, 4, (_, _) => Green) { DelayCentiseconds = 20 })
            .AddSolid(Blue, delayCentiseconds: 5)
            .Build();

        var blocks = GifBlockReader.Read(gif)!;

        WithDecoded(gif, composite =>
        {
            var set = Assert.IsType<VariantRasterContent>(composite.Content);

            Assert.Equal(VariantKind.Frames, set.Kind);
            Assert.Equal(["Frame 1", "Frame 2", "Frame 3"], set.Variants.Select(v => v.Label));
            Assert.Equal(["16x12, 100 ms", "4x4 at 2,3, 200 ms", "16x12, 50 ms"], set.Variants.Select(v => v.Detail));
            Assert.Equal(blocks.Frames.Select(f => (long?)f.EncodedBytes), set.Variants.Select(v => v.ByteSize));

            // Every frame is drawn on the whole canvas, whatever its own rectangle.
            Assert.All(set.Variants, v => Assert.Equal((Width, Height), (v.Width, v.Height)));

            var facts = composite.FormatSpecificSnapshot().ToDictionary(p => p.Key, p => p.Value);
            Assert.Equal("GIF89a", facts["Format"]);
            Assert.Equal("3", facts["Frames"]);
            Assert.Equal("350 ms", facts["Duration"]);
            Assert.Equal("forever", facts["Loop"]);

            // Nothing beyond the frames to inspect, so no structure; the facts are all listed here.
            Assert.Null(composite.Structure);
            Assert.Equal(["Format", "Frames", "Duration", "Loop", "Palette"], facts.Keys);
        });
    }

    [Fact]
    public void TheStreamsDetails_AreListedWithTheFormat()
    {
        var builder = new GifBuilder(Width, Height) { LoopCount = 3 };
        builder.Comments.Add("made by a test");

        var gif = builder
            .AddSolid(Red)
            .Add(new GifBuilder.Frame(2, 3, 4, 5, (_, _) => 1) { Interlaced = true, LocalPalette = [(1, 2, 3), (4, 5, 6)] })
            .Add(new GifBuilder.Frame(0, 0, 2, 2, (_, _) => 0) { TransparentIndex = 0 })
            .Build();

        WithDecoded(gif, composite =>
        {
            var facts = composite.FormatSpecificSnapshot().ToDictionary(p => p.Key, p => p.Value);

            Assert.Equal("3 repeats", facts["Loop"]);
            Assert.Equal("4 colors, 1 local", facts["Palette"]);
            Assert.Equal("1 of 3 frames", facts["Interlaced"]);
            Assert.Equal("1 of 3 frames", facts["Transparency"]);
            Assert.Equal("1", facts["Comments"]);
            Assert.False(facts.ContainsKey("Truncated"));
        });
    }

    [Fact]
    public void AStillGifsDetails_AreYesOrNothing()
    {
        var gif = new GifBuilder(Width, Height)
            .Add(new GifBuilder.Frame(0, 0, Width, Height, (x, _) => x < 8 ? (byte)0 : Green) { TransparentIndex = 0 })
            .Build();

        WithDecoded(gif, composite =>
        {
            var facts = composite.FormatSpecificSnapshot().ToDictionary(p => p.Key, p => p.Value);

            Assert.Equal("yes", facts["Transparency"]);
            Assert.False(facts.ContainsKey("Interlaced"));
            Assert.False(facts.ContainsKey("Loop"));
        });
    }

    [Fact]
    public void AStillGif_IsAPlainImage()
    {
        var gif = new GifBuilder(Width, Height) { LoopCount = null }.AddSolid(Green).Build();

        WithDecoded(gif, composite =>
        {
            Assert.IsNotType<VariantRasterContent>(composite.Content);
            Assert.Equal(Width, composite.Content!.DecodedWidth);
            Assert.Null(composite.Structure);
            Assert.Equal(["Format", "Palette"], composite.FormatSpecificSnapshot().Select(p => p.Key));
        });
    }

    [Fact]
    public void SelectingAFrame_DecodesItOnDemand()
    {
        var gif = new GifBuilder(Width, Height).AddSolid(Red).AddSolid(Green).AddSolid(Blue).Build();

        WithDecoded(gif, composite =>
        {
            var set = Assert.IsType<VariantRasterContent>(composite.Content);

            using var arrived = new ManualResetEventSlim(false);
            set.VariantReady += _ => arrived.Set();
            set.VariantFailed += _ => arrived.Set();

            Assert.True(set.Select(2));
            Assert.True(arrived.Wait(TimeSpan.FromSeconds(10)), "the frame never arrived");

            Assert.Null(set.FailureOf(2));
            Assert.Equal(2, set.ShownIndex);

            var raster = Assert.IsType<RasterContent>(set.Active);
            Assert.Equal(OpaqueBlue, PixelOf(raster.Image, 5, 5));
        });
    }

    [Fact]
    public void AFrame_IsCompositedOverTheFramesBelow()
    {
        var gif = new GifBuilder(Width, Height)
            .AddSolid(Red)
            .Add(new GifBuilder.Frame(4, 4, 4, 4, (_, _) => Green))
            .Add(new GifBuilder.Frame(0, 0, 2, 2, (_, _) => Blue))
            .Build();

        var pixels = Render(gif, 2, (5, 5), (0, 0), (10, 10));

        Assert.Equal([OpaqueGreen, OpaqueBlue, OpaqueRed], pixels);
    }

    [Fact]
    public void RestoreToBackground_ClearsTheFramesRectangle()
    {
        var gif = new GifBuilder(Width, Height)
            .AddSolid(Red)
            .Add(new GifBuilder.Frame(4, 4, 4, 4, (_, _) => Green) { Disposal = 2 })
            .Add(new GifBuilder.Frame(0, 0, 2, 2, (_, _) => Blue))
            .Build();

        Assert.Equal([Clear, OpaqueRed], Render(gif, 2, (5, 5), (10, 10)));
    }

    [Fact]
    public void RestoreToPrevious_PutsBackWhatWasThere()
    {
        var gif = new GifBuilder(Width, Height)
            .AddSolid(Red)
            .Add(new GifBuilder.Frame(4, 4, 4, 4, (_, _) => Green) { Disposal = 3 })
            .Add(new GifBuilder.Frame(0, 0, 2, 2, (_, _) => Blue))
            .Build();

        Assert.Equal([OpaqueRed, OpaqueBlue], Render(gif, 2, (5, 5), (0, 0)));
    }

    [Fact]
    public void TransparentPixels_ShowTheFrameBelow()
    {
        var gif = new GifBuilder(Width, Height)
            .AddSolid(Red)
            .Add(new GifBuilder.Frame(0, 0, Width, Height, (x, _) => x < 8 ? (byte)0 : Green) { TransparentIndex = 0 })
            .Build();

        Assert.Equal([OpaqueRed, OpaqueGreen], Render(gif, 1, (2, 2), (12, 2)));
    }
    
    [Fact]
    public void AnyOrder_RendersTheSameFramesAsAColdStart()
    {
        var builder = new GifBuilder(Width, Height).AddSolid(Red);

        for (var i = 0; i < 9; i++)
        {
            var n = i;
            builder.Add(new GifBuilder.Frame(n, n % 5, 4, 3, (x, y) => (byte)((x + y + n) % 4)) { Disposal = 1 + n % 3, TransparentIndex = 0 });
        }

        var gif = builder.Build();

        using var stream = new MemoryStream(gif);
        using var codec = SKCodec.Create(stream)!;

        var cold = new byte[codec.FrameCount][];
        for (var i = 0; i < cold.Length; i++)
        {
            using var renderer = Renderer(codec);
            cold[i] = Bytes(renderer.Render(codec, i, CancellationToken.None));
        }

        using var warm = Renderer(codec);
        foreach (var i in new[] { 0, 1, 2, 3, 9, 4, 5, 5, 8, 7, 6, 2, 9, 0 })
            Assert.True(cold[i].SequenceEqual(Bytes(warm.Render(codec, i, CancellationToken.None))), $"frame {i} differs when rendered warm");
    }

    [Fact]
    public void ACancelledRender_DoesNotPoisonTheNextOne()
    {
        var gif = new GifBuilder(Width, Height)
            .AddSolid(Red)
            .Add(new GifBuilder.Frame(4, 4, 4, 4, (_, _) => Green))
            .Add(new GifBuilder.Frame(0, 0, 2, 2, (_, _) => Blue))
            .Build();

        using var stream = new MemoryStream(gif);
        using var codec = SKCodec.Create(stream)!;
        using var renderer = Renderer(codec);

        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();

        Assert.ThrowsAny<OperationCanceledException>(() => renderer.Render(codec, 2, cancelled.Token));

        using var frame = renderer.Render(codec, 2, CancellationToken.None);
        Assert.Equal(OpaqueGreen, (uint)frame.GetPixel(5, 5));
    }

    [Fact]
    public void AClosedRenderer_RefusesAsACancellation()
    {
        var gif = new GifBuilder(Width, Height).AddSolid(Red).AddSolid(Green).Build();

        using var stream = new MemoryStream(gif);
        using var codec = SKCodec.Create(stream)!;

        var renderer = Renderer(codec);
        renderer.Render(codec, 1, CancellationToken.None).Dispose();
        renderer.Dispose();
        renderer.Dispose();

        Assert.Throws<OperationCanceledException>(() => renderer.Render(codec, 0, CancellationToken.None));
    }

    [Fact]
    public void TheThumbnail_IsTheFirstFrame()
    {
        var gif = new GifBuilder(Width, Height).AddSolid(Red).AddSolid(Blue).Build();
        using var file = new TempFile(gif);

        using var thumbnail = new GifDecoder().DecodeThumbnail(file.Path, 32, CancellationToken.None);

        Assert.NotNull(thumbnail);
        Assert.Equal(OpaqueRed, (uint)thumbnail.GetPixel(1, 1));
    }

    [Fact]
    public void ATruncatedAnimation_StillOpens()
    {
        var gif = new GifBuilder(Width, Height).AddSolid(Red).AddSolid(Green).AddSolid(Blue).Build();
        var blocks = GifBlockReader.Read(gif)!;

        WithDecoded(gif[..^((int)blocks.Frames[2].EncodedBytes / 2)], composite =>
        {
            Assert.NotNull(composite.Content);
            Assert.Contains(composite.FormatSpecificSnapshot(), p => p.Key == "Truncated");
        });
    }

    [Fact]
    public void AGifWithNoImages_SaysSo_AndKeepsItsFacts()
    {
        var (message, facts) = Failure(new GifBuilder(Width, Height).Build());

        Assert.Equal("The GIF holds no frames", message);
        Assert.Equal("GIF89a", facts["Format"]);
        Assert.Equal("none", facts["Frames"]);
        Assert.Equal("4 colors", facts["Palette"]);
    }

    [Fact]
    public void AGifCutBeforeItsFirstImage_SaysSo_AndKeepsItsFacts()
    {
        var gif = new GifBuilder(Width, Height).AddRaw(0x2C, 0, 0).Build()[..^1];

        var (message, facts) = Failure(gif);

        Assert.Equal("The GIF ends before its first frame", message);
        Assert.Equal("none", facts["Frames"]);
        Assert.True(facts.ContainsKey("Truncated"));
    }
    
    [Theory]
    [InlineData(new byte[] { 0x02, 0x01, 0x05, 0x00 }, "Frame 1 has no pixel data")]
    [InlineData(new byte[] { 0x02, 0x01, 0x28, 0x00 }, "Frame 1 holds 1 of its 4 pixels")]
    [InlineData(new byte[] { 0x02, 0x01, 0x07, 0x00 }, "Frame 1's pixel data is corrupt after 0 of 4 pixels")]
    [InlineData(new byte[] { 0x09, 0x01, 0x00, 0x00 }, "Frame 1 declares an invalid LZW code size (9)")]
    public void BrokenPixelData_IsShownAsFarAsItGoes_WithTheReason(byte[] pixelData, string expected)
    {
        WithDecoded(new GifBuilder(Width, Height).AddRaw(RawFrame(pixelData)).Build(), composite =>
        {
            Assert.IsType<RasterContent>(composite.Content);

            var facts = composite.FormatSpecificSnapshot().ToDictionary(p => p.Key, p => p.Value);
            Assert.Equal(expected, facts["Damaged"]);
            Assert.Equal("GIF89a", facts["Format"]);
            Assert.Equal(LoadWarning.PartiallyDecoded(expected), composite.Warning);
        });
    }

    [Fact]
    public void APartialFrame_KeepsWhatWasDecoded()
    {
        var gif = WithFrameCut(new GifBuilder(Width, Height).AddSolid(Red).AddSolid(Green).Build(), frame: 1, keepBytes: 30);

        using var stream = new MemoryStream(gif);
        using var codec = SKCodec.Create(stream)!;
        using var renderer = new GifFrameRenderer(Renderer(codec).Info, Explain(gif));
        using var bitmap = renderer.Render(codec, 1, CancellationToken.None);

        // 30 bytes of nine-bit codes, less the opening clear, is 25 pixels: one row and nine more.
        var decoded = new[] { (uint)bitmap.GetPixel(0, 0), (uint)bitmap.GetPixel(15, 0), (uint)bitmap.GetPixel(8, 1) };
        Assert.Equal([OpaqueGreen, OpaqueGreen, OpaqueGreen], decoded);

        // Skia clears what it did not reach rather than leaving the frame below.
        Assert.Equal(Clear, (uint)bitmap.GetPixel(9, 1));
        Assert.Equal(Clear, (uint)bitmap.GetPixel(0, 5));
    }

    [Fact]
    public void ADamagedFirstFrame_IsMarkedInTheFrameList()
    {
        var gif = new GifBuilder(Width, Height).AddRaw(RawFrame(0x02, 0x01, 0x28, 0x00)).AddSolid(Blue).Build();

        WithDecoded(gif, composite =>
        {
            var set = Assert.IsType<VariantRasterContent>(composite.Content);

            Assert.Equal(LoadWarning.PartiallyDecoded("Frame 1 holds 1 of its 4 pixels"), set.WarningOf(0));
            Assert.Null(set.WarningOf(1));
            Assert.Null(composite.Warning);
        });
    }

    [Fact]
    public void AStillGifCutMidFrame_IsShownAndFlagged()
    {
        var gif = new GifBuilder(Width, Height).AddSolid(Green).Build();
        var frame = GifBlockReader.Read(gif)!.Frames[0];

        WithDecoded(gif[..(frame.DataOffset + frame.DataLength / 2)], composite =>
        {
            Assert.IsType<RasterContent>(composite.Content);
            Assert.Equal(LoadWarning.PartiallyDecoded("Frame 1 is cut short: the file ends inside it"), composite.Warning);
            Assert.Contains(composite.FormatSpecificSnapshot(), p => p is { Key: "Damaged", Value: "Frame 1 is cut short: the file ends inside it" });
        });
    }

    [Fact]
    public void ADamagedFrame_IsReportedOnce()
    {
        var gif = new GifBuilder(Width, Height).AddSolid(Red).AddRaw(RawFrame(0x02, 0x01, 0x28, 0x00)).AddSolid(Blue).Build();
        var reports = new List<(int, string)>();

        using var stream = new MemoryStream(gif);
        using var codec = SKCodec.Create(stream)!;
        using var renderer = new GifFrameRenderer(Renderer(codec).Info, Explain(gif), (index, why) => reports.Add((index, why)));

        foreach (var frame in new[] { 1, 2, 0, 2, 1 })
            renderer.Render(codec, frame, CancellationToken.None).Dispose();

        Assert.Equal([(1, "Frame 2 holds 1 of its 4 pixels")], reports);
    }

    [Fact]
    public void AFailureNothingExplains_StillFails()
    {
        var gif = new GifBuilder(Width, Height).AddRaw(RawFrame(0x02, 0x01, 0x28, 0x00)).Build();

        using var stream = new MemoryStream(gif);
        using var codec = SKCodec.Create(stream)!;
        using var renderer = new GifFrameRenderer(Renderer(codec).Info, explain: _ => null);

        var thrown = Assert.Throws<LoadFailureException>(() => renderer.Render(codec, 0, CancellationToken.None));
        Assert.Equal("Frame 1 could not be decoded (ErrorInInput)", thrown.Message);
    }

    [Fact]
    public void SeveralDamagedFrames_AreListedByNumber()
    {
        Assert.Equal("Frame 3 holds 1 of its 4 pixels", GifFrameSet.DescribeDamage(new Dictionary<int, string> { [2] = "Frame 3 holds 1 of its 4 pixels" }));
        Assert.Equal("2 frames: 3, 8", GifFrameSet.DescribeDamage(new Dictionary<int, string> { [7] = "b", [2] = "a" }));
        Assert.Equal("6 frames: 1, 2, 3, 4, 5, ...", GifFrameSet.DescribeDamage(Enumerable.Range(0, 6).ToDictionary(i => i, _ => "x")));
    }

    [Fact]
    public void ABrokenLaterFrame_IsShownAndReportedWhenSelected()
    {
        var gif = new GifBuilder(Width, Height).AddSolid(Red).AddRaw(RawFrame(0x02, 0x01, 0x28, 0x00)).Build();

        WithDecoded(gif, composite =>
        {
            var set = Assert.IsType<VariantRasterContent>(composite.Content);

            using var settled = new ManualResetEventSlim(false);
            set.VariantFailed += _ => settled.Set();
            set.VariantReady += _ => settled.Set();

            Assert.DoesNotContain(composite.FormatSpecificSnapshot(), p => p.Key == "Damaged");

            Assert.True(set.Select(1));
            Assert.True(settled.Wait(TimeSpan.FromSeconds(10)), "the frame never settled");

            Assert.Null(set.FailureOf(1));
            Assert.Equal(1, set.ShownIndex);
            Assert.Equal(LoadWarning.PartiallyDecoded("Frame 2 holds 1 of its 4 pixels"), set.WarningOf(1));
            Assert.Null(set.WarningOf(0));
            Assert.Contains(composite.FormatSpecificSnapshot(), p => p is { Key: "Damaged", Value: "Frame 2 holds 1 of its 4 pixels" });
        });
    }

    [Fact]
    public void PixelData_IsCountedAcrossClearCodes_AndSurplusIsNoFault()
    {
        var gif = new GifBuilder(Width, Height).AddSolid(Green).Build();
        var frame = Assert.Single(GifBlockReader.Read(gif)!.Frames);
        var data = gif.AsSpan(frame.DataOffset, frame.DataLength);

        Assert.Null(GifPixelData.Explain(data, frame, 0));
        Assert.Null(GifPixelData.Explain(data, frame with { Width = 2, Height = 2 }, 0));
        Assert.Equal("Frame 1 holds 192 of its 1024 pixels", GifPixelData.Explain(data, frame with { Width = 32, Height = 32 }, 0));
    }

    private static Func<int, string?> Explain(byte[] gif)
    {
        var blocks = GifBlockReader.Read(gif)!;
        return index => GifPixelData.Explain(gif.AsSpan(blocks.Frames[index].DataOffset, blocks.Frames[index].DataLength), blocks.Frames[index], index);
    }

    private static byte[] WithFrameCut(byte[] gif, int frame, int keepBytes)
    {
        var data = GifBlockReader.Read(gif)!.Frames[frame];
        var after = data.DataOffset + data.DataLength;

        return [.. gif[..data.DataOffset], gif[data.DataOffset], (byte)keepBytes, .. gif.AsSpan(data.DataOffset + 2, keepBytes), 0, .. gif[after..]];
    }

    private static byte[] RawFrame(params byte[] pixelData) => [0x2C, 0, 0, 0, 0, 2, 0, 2, 0, 0x00, .. pixelData];

    private static (string Message, Dictionary<string, string> Facts) Failure(byte[] gif)
    {
        using var file = new TempFile(gif);
        using var composite = new Composite(new FileInfo(file.Path));

        var thrown = Assert.Throws<LoadFailureException>(() => new GifDecoder().DecodeAsync(composite, CancellationToken.None).GetAwaiter().GetResult());

        return (thrown.Message, composite.FormatSpecificSnapshot().ToDictionary(p => p.Key, p => p.Value));
    }

    private static void WithDecoded(byte[] gif, Action<Composite> assert)
    {
        using var file = new TempFile(gif);
        using var composite = new Composite(new FileInfo(file.Path));

        new GifDecoder().DecodeAsync(composite, CancellationToken.None).GetAwaiter().GetResult();

        assert(composite);
    }

    private static GifFrameRenderer Renderer(SKCodec codec) =>
        new(new SKImageInfo(codec.Info.Width, codec.Info.Height, SKColorType.Rgba8888, SKAlphaType.Premul, SKColorSpace.CreateSrgb()));

    private static uint[] Render(byte[] gif, int frame, params (int X, int Y)[] points)
    {
        using var stream = new MemoryStream(gif);
        using var codec = SKCodec.Create(stream)!;
        using var renderer = Renderer(codec);
        using var bitmap = renderer.Render(codec, frame, CancellationToken.None);

        return [.. points.Select(p => (uint)bitmap.GetPixel(p.X, p.Y))];
    }

    private static byte[] Bytes(SKBitmap bitmap)
    {
        using (bitmap)
            return bitmap.GetPixelSpan().ToArray();
    }

    private static uint PixelOf(SKImage image, int x, int y)
    {
        using var bitmap = SKBitmap.FromImage(image);
        return (uint)bitmap.GetPixel(x, y);
    }
}