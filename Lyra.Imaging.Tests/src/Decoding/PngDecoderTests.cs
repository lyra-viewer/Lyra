using Lyra.Common;
using Lyra.Imaging.Content;
using Lyra.Imaging.Decoding.Decoders;
using Lyra.Imaging.Decoding.Decoders.Animation;
using Lyra.Imaging.Loading;
using Lyra.Imaging.Tests.Support;
using Lyra.ManagedCodecs.Raster.Png;
using Lyra.ManagedCodecs.Tests.Png;
using SkiaSharp;
using Xunit;

namespace Lyra.Imaging.Tests.Decoding;

public class PngDecoderTests
{
    private const uint Red = 0xFFFF0000, Green = 0xFF00FF00, Blue = 0xFF0000FF, White = 0xFFFFFFFF, Clear = 0;

    private const int Width = 16, Height = 12;

    [Fact]
    public void PngIsRegistered()
    {
        Assert.Equal(ImageFormatType.Png, ImageFormat.GetImageFormat(".png"));
        Assert.IsType<PngDecoder>(DecoderManager.GetDecoder(ImageFormatType.Png));
    }

    [Fact]
    public void AnAnimation_IsPublishedAsFrames()
    {
        var png = new ApngBuilder(Width, Height)
            .AddSolid(Red, delayMs: 100)
            .Add(new ApngBuilder.Frame(2, 4, 4, 4, (_, _) => Green) { DelayNumerator = 20, DelayDenominator = 100 })
            .AddSolid(Blue, delayMs: 50)
            .Build();

        var chunks = ApngChunkReader.Read(png)!;

        WithDecoded(png, composite =>
        {
            var set = Assert.IsType<VariantRasterContent>(composite.Content);

            Assert.Equal(VariantKind.Frames, set.Kind);
            Assert.Equal(["Frame 1", "Frame 2", "Frame 3"], set.Variants.Select(v => v.Label));
            Assert.Equal(["16x12, 100 ms", "4x4 at 2,4, 200 ms", "16x12, 50 ms"], set.Variants.Select(v => v.Detail));
            Assert.Equal(chunks.Frames.Select(f => (long?)f.EncodedBytes), set.Variants.Select(v => v.ByteSize));
            Assert.All(set.Variants, v => Assert.Equal((Width, Height), (v.Width, v.Height)));

            var facts = Facts(composite);
            Assert.Equal(["Frames", "Duration", "Loop"], facts.Keys);
            Assert.Equal("3", facts["Frames"]);
            Assert.Equal("350 ms", facts["Duration"]);
            Assert.Equal("forever", facts["Loop"]);

            Assert.Equal(Red, PixelOf(set.Active, 0, 0));
            Assert.Null(composite.Structure);
        });
    }

    [Theory]
    [InlineData(0, "forever")]
    [InlineData(1, "plays once")]
    [InlineData(2, "1 repeat")]
    [InlineData(4, "3 repeats")]
    public void TheLoopCount_CountsPlays(int plays, string expected)
    {
        var png = new ApngBuilder(Width, Height) { Plays = plays }.AddSolid(Red).AddSolid(Blue).Build();

        WithDecoded(png, composite => Assert.Equal(expected, Facts(composite)["Loop"]));
    }

    [Fact]
    public void AStillPng_TakesThePlainPath()
    {
        WithDecoded(new ApngBuilder(Width, Height) { Plays = null }.AddSolid(Green).Build(), composite =>
        {
            Assert.Equal(Green, PixelOf(Assert.IsType<RasterContent>(composite.Content), 3, 3));
            Assert.Empty(composite.FormatSpecificSnapshot());
        });
    }

    [Fact]
    public void ASingleFrameAnimation_IsAPlainImage()
    {
        WithDecoded(new ApngBuilder(Width, Height).AddSolid(Red).Build(), composite =>
        {
            Assert.Equal(Red, PixelOf(Assert.IsType<RasterContent>(composite.Content), 3, 3));
            Assert.Empty(composite.FormatSpecificSnapshot());
        });
    }

    [Fact]
    public void AHiddenDefaultImage_IsLeftOut_AndSaidSo()
    {
        var png = new ApngBuilder(Width, Height) { HiddenDefault = (_, _) => White }.AddSolid(Red).AddSolid(Blue).Build();

        WithDecoded(png, composite =>
        {
            var set = Assert.IsType<VariantRasterContent>(composite.Content);

            Assert.Equal(2, set.Variants.Count);
            Assert.Equal(Red, PixelOf(set.Active, 3, 3));
            Assert.Equal("a fallback, not part of the animation", Facts(composite)["Default Image"]);
        });
    }

    [Fact]
    public void SelectingAFrame_DecodesItOnDemand()
    {
        var png = new ApngBuilder(Width, Height).AddSolid(Red).AddSolid(Red).AddSolid(Blue).Build();

        WithDecoded(png, composite =>
        {
            var set = Assert.IsType<VariantRasterContent>(composite.Content);

            Assert.True(SelectAndWait(set, 2));
            Assert.Null(set.FailureOf(2));
            Assert.Equal(Blue, PixelOf(set.Active, 5, 5));
        });
    }

    [Fact]
    public void BlendOver_ShowsTheFrameBelowThroughClearPixels()
    {
        var png = new ApngBuilder(Width, Height)
            .AddSolid(Red)
            .Add(new ApngBuilder.Frame(4, 4, 4, 4, (x, _) => x < 2 ? Clear : Green) { Blend = 1 })
            .Build();

        Assert.Equal([Red, Green, Red], Render(png, 1, (4, 4), (6, 4), (0, 0)));
    }

    [Fact]
    public void BlendSource_ReplacesThePixelsBelow()
    {
        var png = new ApngBuilder(Width, Height)
            .AddSolid(Red)
            .Add(new ApngBuilder.Frame(4, 4, 4, 4, (x, _) => x < 2 ? Clear : Green))
            .Build();

        Assert.Equal([Clear, Green, Red], Render(png, 1, (4, 4), (6, 4), (0, 0)));
    }

    [Fact]
    public void DisposeToBackground_ClearsTheFramesRectangle()
    {
        var png = new ApngBuilder(Width, Height)
            .AddSolid(Red)
            .Add(new ApngBuilder.Frame(4, 4, 4, 4, (_, _) => Green) { Dispose = 1 })
            .Add(new ApngBuilder.Frame(0, 0, 2, 2, (_, _) => Blue))
            .Build();

        Assert.Equal([Clear, Blue, Red], Render(png, 2, (5, 5), (0, 0), (10, 10)));
    }

    [Fact]
    public void DisposeToPrevious_PutsBackWhatWasThere()
    {
        var png = new ApngBuilder(Width, Height)
            .AddSolid(Red)
            .Add(new ApngBuilder.Frame(4, 4, 4, 4, (_, _) => Green) { Dispose = 2 })
            .Add(new ApngBuilder.Frame(0, 0, 2, 2, (_, _) => Blue))
            .Build();

        Assert.Equal([Red, Blue], Render(png, 2, (5, 5), (0, 0)));
    }

    [Fact]
    public void AFirstFrameDisposingToPrevious_DisposesToBackground()
    {
        var png = new ApngBuilder(Width, Height)
            .Add(new ApngBuilder.Frame(0, 0, Width, Height, (_, _) => Red) { Dispose = 2 })
            .Add(new ApngBuilder.Frame(0, 0, 2, 2, (_, _) => Blue) { Blend = 1 })
            .Build();

        Assert.Equal([Clear, Blue], Render(png, 1, (5, 5), (0, 0)));
    }

    [Fact]
    public void APaletteAnimation_DecodesEveryFrameWithThePalette()
    {
        var png = new ApngBuilder(Width, Height) { Palette = [Clear, Red, Green, Blue] }
            .AddSolid(Red)
            .Add(new ApngBuilder.Frame(4, 4, 4, 4, (x, _) => x < 2 ? Clear : Green) { Blend = 1 })
            .Build();

        Assert.Equal([Red, Green, Red], Render(png, 1, (4, 4), (6, 4), (0, 0)));
    }

    [Fact]
    public void AnyOrder_RendersTheSameFramesAsAColdStart()
    {
        var builder = new ApngBuilder(Width, Height).AddSolid(Red);

        for (var i = 0; i < 12; i++)
        {
            var n = i;
            var frame = n == 6
                ? new ApngBuilder.Frame(0, 0, Width, Height, (x, y) => (x + y) % 2 == 0 ? Blue : Green)
                : new ApngBuilder.Frame(n % 4 * 3, n % 3 * 2, 4, 3, (x, y) => (x + y + n) % 3 == 0 ? Clear : 0xFF000000 | (uint)(n * 20) << 16);

            builder.Add(frame with { Dispose = (byte)(n % 3), Blend = (byte)(n % 2) });
        }

        var png = builder.Build();
        var chunks = ApngChunkReader.Read(png)!;

        var cold = new byte[chunks.Frames.Count][];
        for (var i = 0; i < cold.Length; i++)
        {
            using var source = Source(png, chunks);
            cold[i] = Bytes(source.Render(i, CancellationToken.None));
        }

        using var warm = Source(png, chunks);
        foreach (var i in new[] { 0, 1, 2, 3, 12, 4, 5, 5, 8, 7, 6, 7, 9, 2, 12, 0, 11 })
            Assert.True(cold[i].SequenceEqual(Bytes(warm.Render(i, CancellationToken.None))), $"frame {i} differs when rendered warm");
    }

    [Fact]
    public void AClosedSource_RefusesAsACancellation()
    {
        var png = new ApngBuilder(Width, Height).AddSolid(Red).AddSolid(Green).Build();

        var source = Source(png, ApngChunkReader.Read(png)!);
        source.Render(1, CancellationToken.None).Dispose();
        source.Dispose();
        source.Dispose();

        Assert.Throws<OperationCanceledException>(() => source.Render(0, CancellationToken.None));
    }

    [Fact]
    public void TheThumbnail_IsTheDefaultImage()
    {
        using var file = new TempFile(new ApngBuilder(Width, Height) { HiddenDefault = (_, _) => White }.AddSolid(Red).AddSolid(Blue).Build());

        using var thumbnail = new PngDecoder().DecodeThumbnail(file.Path, 32, CancellationToken.None);

        Assert.NotNull(thumbnail);
        Assert.Equal(White, (uint)thumbnail.GetPixel(1, 1));
    }

    [Fact]
    public void AFrameCutShort_IsShownAsFarAsItGoes_AndFlagged()
    {
        var png = new ApngBuilder(Width, Height).AddSolid(Red).AddSolid(Green).AddSolid(Blue).Build();
        var last = ApngChunkReader.Read(png)!.Frames[2].Data[0];

        WithDecoded(png[..(int)(last.Offset + last.Length / 2)], composite =>
        {
            var set = Assert.IsType<VariantRasterContent>(composite.Content);
            Assert.Equal("stops before the end chunk", Facts(composite)["Truncated"]);

            Assert.True(SelectAndWait(set, 2));
            Assert.Null(set.FailureOf(2));
            Assert.Equal(LoadWarning.PartiallyDecoded("Frame 3 is cut short: the file ends inside it"), set.WarningOf(2));
        });
    }

    [Fact]
    public void AFileCutBeforeAFramesData_SaysSo()
    {
        var png = new ApngBuilder(Width, Height).AddSolid(Red).AddSolid(Green).Build();
        var last = ApngChunkReader.Read(png)!.Frames[1].Data[0];

        WithDecoded(png[..(int)(last.Offset - 2)], composite =>
        {
            var set = Assert.IsType<VariantRasterContent>(composite.Content);

            Assert.True(SelectAndWait(set, 1));
            Assert.Equal(LoadWarning.PartiallyDecoded("Frame 2 is cut off: the file ends before its image data"), set.WarningOf(1));
        });
    }

    [Fact]
    public void AFrameWithNoData_IsReported_AndTheRestStillShow()
    {
        var png = new ApngBuilder(Width, Height).AddSolid(Red).AddSolid(Green).AddSolid(Blue).Build();
        var data = ApngChunkReader.Read(png)!.Frames[1].Data[0];
        var fdat = (int)data.Offset - 12;

        WithDecoded([.. png[..fdat], .. png[(fdat + 16 + data.Length)..]], composite =>
        {
            var set = Assert.IsType<VariantRasterContent>(composite.Content);

            Assert.True(SelectAndWait(set, 1));
            Assert.Equal(LoadWarning.PartiallyDecoded("Frame 2 has no image data"), set.WarningOf(1));
            Assert.Equal(Clear, PixelOf(set.Active, 3, 3));

            Assert.True(SelectAndWait(set, 2));
            Assert.Equal(Blue, PixelOf(set.Active, 3, 3));
        });
    }

    [Fact]
    public void CorruptFrameData_IsReported_NotFatal()
    {
        var png = new ApngBuilder(Width, Height).AddSolid(Red).AddSolid(Green).Build();
        var data = ApngChunkReader.Read(png)!.Frames[1].Data[0];
        png.AsSpan((int)data.Offset, data.Length).Fill(0x5A);

        WithDecoded(png, composite =>
        {
            var set = Assert.IsType<VariantRasterContent>(composite.Content);

            Assert.True(SelectAndWait(set, 1));
            Assert.Null(set.FailureOf(1));
            Assert.Equal(LoadWarning.PartiallyDecoded("Frame 2's image data is corrupt"), set.WarningOf(1));
        });
    }

    [Fact]
    public void AFrameOutsideTheCanvas_IsNotDrawn()
    {
        var png = new ApngBuilder(Width, Height)
            .AddSolid(Red)
            .Add(new ApngBuilder.Frame(12, 0, 8, 8, (_, _) => Green) { Blend = 1 })
            .Build();

        var reports = new List<(int, string)>();
        var chunks = ApngChunkReader.Read(png)!;

        using var source = Source(png, chunks, (index, why) => reports.Add((index, why)));
        using var frame = source.Render(1, CancellationToken.None);

        Assert.Equal(Red, (uint)frame.GetPixel(14, 2));
        Assert.Equal([(1, "Frame 2 lies outside the 16x12 canvas, so is not drawn")], reports);
    }

    [Fact]
    public void AMisstatedFrameCount_IsShown()
    {
        var png = new ApngBuilder(Width, Height) { DeclaredFrames = 5 }.AddSolid(Red).AddSolid(Blue).Build();

        WithDecoded(png, composite => Assert.Equal("5", Facts(composite)["Declared Frames"]));
    }

    [Fact]
    public void AnAnimationWithNoFrames_ShowsTheDefaultImage()
    {
        var png = new ApngBuilder(Width, Height) { HiddenDefault = (_, _) => White, DeclaredFrames = 2 }.Build();

        WithDecoded(png, composite =>
        {
            Assert.Equal(White, PixelOf(Assert.IsType<RasterContent>(composite.Content), 3, 3));
            Assert.Equal("none", Facts(composite)["Frames"]);
        });
    }

    private static Dictionary<string, string> Facts(Composite composite) =>
        composite.FormatSpecificSnapshot().ToDictionary(p => p.Key, p => p.Value);

    private static bool SelectAndWait(VariantRasterContent set, int index)
    {
        using var settled = new ManualResetEventSlim(false);
        void Settle(VariantRasterContent _) => settled.Set();

        set.VariantReady += Settle;
        set.VariantFailed += Settle;

        try
        {
            return set.Select(index) && settled.Wait(TimeSpan.FromSeconds(10)) && set.ShownIndex == index;
        }
        finally
        {
            set.VariantReady -= Settle;
            set.VariantFailed -= Settle;
        }
    }

    private static void WithDecoded(byte[] png, Action<Composite> assert)
    {
        using var file = new TempFile(png);
        using var composite = new Composite(new FileInfo(file.Path));

        new PngDecoder().DecodeAsync(composite, CancellationToken.None).GetAwaiter().GetResult();

        assert(composite);
    }

    private static ApngFrameRenderer Source(byte[] png, ApngChunks chunks, Action<int, string>? damaged = null) =>
        new(() => new MemoryStream(png, writable: false), chunks, new SKImageInfo(chunks.Width, chunks.Height, SKColorType.Rgba8888, SKAlphaType.Premul, SKColorSpace.CreateSrgb()), damaged);

    private static uint[] Render(byte[] png, int frame, params (int X, int Y)[] points)
    {
        using var source = Source(png, ApngChunkReader.Read(png)!);
        using var bitmap = source.Render(frame, CancellationToken.None);

        return [.. points.Select(p => (uint)bitmap.GetPixel(p.X, p.Y))];
    }

    private static byte[] Bytes(SKBitmap bitmap)
    {
        using (bitmap)
            return bitmap.GetPixelSpan().ToArray();
    }

    private static uint PixelOf(ICompositeContent? content, int x, int y)
    {
        var raster = Assert.IsType<RasterContent>(content);
        using var bitmap = SKBitmap.FromImage(raster.Image);
        return (uint)bitmap.GetPixel(x, y);
    }
}
