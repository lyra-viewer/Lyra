using Lyra.Common;
using Lyra.Imaging.Content;
using Lyra.Imaging.Decoding.Decoders;
using Lyra.Imaging.Decoding.Decoders.Animation;
using Lyra.Imaging.Loading;
using Lyra.Imaging.Tests.Support;
using Lyra.ManagedCodecs.Raster.Webp;
using Lyra.ManagedCodecs.Tests.Webp;
using SkiaSharp;
using Xunit;

namespace Lyra.Imaging.Tests.Decoding;

public class WebpDecoderTests
{
    private const uint OpaqueRed = 0xFFFF0000, OpaqueGreen = 0xFF00FF00, OpaqueBlue = 0xFF0000FF, Clear = 0;

    private const int Width = 16, Height = 12;

    private static readonly byte[] Red = WebpFrames.Solid(Width, Height, SKColors.Red);
    private static readonly byte[] Blue = WebpFrames.Solid(Width, Height, SKColors.Blue);
    private static readonly byte[] GreenSquare = WebpFrames.Solid(4, 4, SKColors.Lime);

    [Fact]
    public void WebpIsRegistered()
    {
        Assert.Equal(ImageFormatType.Webp, ImageFormat.GetImageFormat(".webp"));
        Assert.IsType<WebpDecoder>(DecoderManager.GetDecoder(ImageFormatType.Webp));
    }

    [Fact]
    public void AnAnimation_IsPublishedAsFrames()
    {
        var webp = new WebpBuilder(Width, Height) { LoopCount = 0 }
            .AddFull(Red, durationMs: 100)
            .Add(new WebpBuilder.Frame(2, 4, 4, 4, GreenSquare) { DurationMs = 200 })
            .AddFull(Blue, durationMs: 50)
            .Build();

        var chunks = WebpChunkReader.Read(webp)!;

        WithDecoded(webp, composite =>
        {
            var set = Assert.IsType<VariantRasterContent>(composite.Content);

            Assert.Equal(VariantKind.Frames, set.Kind);
            Assert.Equal(["Frame 1", "Frame 2", "Frame 3"], set.Variants.Select(v => v.Label));
            Assert.Equal(["16x12, 100 ms", "4x4 at 2,4, 200 ms", "16x12, 50 ms"], set.Variants.Select(v => v.Detail));
            Assert.Equal(chunks.Frames.Select(f => (long?)f.EncodedBytes), set.Variants.Select(v => v.ByteSize));
            Assert.All(set.Variants, v => Assert.Equal((Width, Height), (v.Width, v.Height)));

            var facts = Facts(composite);
            Assert.Equal(["Compression", "Frames", "Duration", "Loop"], facts.Keys);
            Assert.Equal("lossless", facts["Compression"]);
            Assert.Equal("3", facts["Frames"]);
            Assert.Equal("350 ms", facts["Duration"]);
            Assert.Equal("forever", facts["Loop"]);

            Assert.Null(composite.Structure);
        });
    }

    [Fact]
    public void MixedFrames_SayHowManyAreLossless()
    {
        var webp = new WebpBuilder(Width, Height)
            .AddFull(Red)
            .AddFull(WebpFrames.Solid(Width, Height, SKColors.Blue, lossless: false))
            .AddFull(WebpFrames.Encode(Width, Height, (x, _) => x < 8 ? SKColors.Transparent : SKColors.Lime))
            .Build();

        WithDecoded(webp, composite =>
        {
            Assert.Equal("2 of 3 frames lossless", Facts(composite)["Compression"]);
        });
    }

    [Theory]
    [InlineData(0, "forever")]
    [InlineData(1, "plays once")]
    [InlineData(2, "1 repeat")]
    [InlineData(4, "3 repeats")]
    public void TheLoopCount_CountsPlays(int loopCount, string expected)
    {
        var webp = new WebpBuilder(Width, Height) { LoopCount = loopCount }.AddFull(Red).AddFull(Blue).Build();

        WithDecoded(webp, composite => Assert.Equal(expected, Facts(composite)["Loop"]));
    }

    [Fact]
    public void AStillWebp_TakesThePlainPath()
    {
        using var bitmap = new SKBitmap(new SKImageInfo(Width, Height, SKColorType.Rgba8888, SKAlphaType.Premul));
        bitmap.Erase(SKColors.Lime);
        using var pixmap = bitmap.PeekPixels();
        using var data = pixmap.Encode(new SKWebpEncoderOptions(SKWebpEncoderCompression.Lossless, 100))!;

        WithDecoded(data.ToArray(), composite =>
        {
            var raster = Assert.IsType<RasterContent>(composite.Content);
            Assert.Equal(OpaqueGreen, PixelOf(raster.Image, 3, 3));
            Assert.Empty(composite.FormatSpecificSnapshot());
        });
    }

    [Fact]
    public void ASingleFrameAnimation_IsAPlainImage()
    {
        WithDecoded(new WebpBuilder(Width, Height).AddFull(Red).Build(), composite =>
        {
            var raster = Assert.IsType<RasterContent>(composite.Content);
            Assert.Equal(OpaqueRed, PixelOf(raster.Image, 3, 3));
            Assert.Equal(["Compression"], Facts(composite).Keys);
        });
    }

    [Fact]
    public void SelectingAFrame_DecodesItOnDemand()
    {
        var webp = new WebpBuilder(Width, Height).AddFull(Red).AddFull(Red).AddFull(Blue).Build();

        WithDecoded(webp, composite =>
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
    public void AFrame_IsBlendedOverTheFrameBelow()
    {
        var halfClear = WebpFrames.Encode(4, 4, (x, _) => x < 2 ? SKColors.Transparent : SKColors.Lime);

        var webp = new WebpBuilder(Width, Height)
            .AddFull(Red)
            .Add(new WebpBuilder.Frame(4, 4, 4, 4, halfClear))
            .Build();

        Assert.Equal([OpaqueRed, OpaqueGreen, OpaqueRed], Render(webp, 1, (4, 4), (6, 4), (0, 0)));
    }

    [Fact]
    public void AFrameThatDoesNotBlend_ReplacesThePixelsBelow()
    {
        var halfClear = WebpFrames.Encode(4, 4, (x, _) => x < 2 ? SKColors.Transparent : SKColors.Lime);

        var webp = new WebpBuilder(Width, Height)
            .AddFull(Red)
            .Add(new WebpBuilder.Frame(4, 4, 4, 4, halfClear) { Blend = false })
            .Build();

        Assert.Equal([Clear, OpaqueGreen, OpaqueRed], Render(webp, 1, (4, 4), (6, 4), (0, 0)));
    }

    [Fact]
    public void DisposeToBackground_ClearsTheFramesRectangle()
    {
        var webp = new WebpBuilder(Width, Height)
            .AddFull(Red)
            .Add(new WebpBuilder.Frame(4, 4, 4, 4, GreenSquare) { DisposeToBackground = true })
            .Add(new WebpBuilder.Frame(0, 0, 2, 2, WebpFrames.Solid(2, 2, SKColors.Blue)))
            .Build();

        Assert.Equal([Clear, OpaqueBlue, OpaqueRed], Render(webp, 2, (5, 5), (0, 0), (10, 10)));
    }

    [Fact]
    public void AnyOrder_RendersTheSameFramesAsAColdStart()
    {
        var builder = new WebpBuilder(Width, Height).AddFull(Red);

        for (var i = 0; i < 9; i++)
        {
            var n = i;
            var square = WebpFrames.Encode(4, 4, (x, y) => (x + y + n) % 3 == 0 ? SKColors.Transparent : new SKColor((byte)(n * 25), 0, (byte)(255 - n * 25)));
            builder.Add(new WebpBuilder.Frame(n % 4 * 2, n % 3 * 2, 4, 4, square) { Blend = n % 2 == 0, DisposeToBackground = n % 3 == 1 });
        }

        var webp = builder.Build();

        using var stream = new MemoryStream(webp);
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
    public void TheThumbnail_IsTheFirstFrame()
    {
        using var file = new TempFile(new WebpBuilder(Width, Height).AddFull(Red).AddFull(Blue).Build());

        using var thumbnail = new WebpDecoder().DecodeThumbnail(file.Path, 32, CancellationToken.None);

        Assert.NotNull(thumbnail);
        Assert.Equal(OpaqueRed, (uint)thumbnail.GetPixel(1, 1));
    }

    [Fact]
    public void ATruncatedAnimation_StillOpens()
    {
        var webp = new WebpBuilder(Width, Height).AddFull(Red).AddFull(Red).AddFull(Blue).Build();

        WithDecoded(webp[..^(int)(WebpChunkReader.Read(webp)!.Frames[2].EncodedBytes / 2)], composite =>
        {
            var set = Assert.IsType<VariantRasterContent>(composite.Content);
            Assert.Equal(2, set.Variants.Count);
            Assert.Equal("ends inside frame 3, which is not shown", Facts(composite)["Truncated"]);
        });
    }

    [Fact]
    public void ACorruptFrame_FailsAlone_WhenSelected()
    {
        var webp = new WebpBuilder(Width, Height).AddFull(Red).AddFull(Red).AddFull(Blue).Build();
        var last = (int)WebpChunkReader.Read(webp)!.Frames[2].EncodedBytes - 40;

        WithDecoded([.. webp[..^last], .. new byte[last]], composite =>
        {
            var set = Assert.IsType<VariantRasterContent>(composite.Content);

            using var settled = new ManualResetEventSlim(false);
            set.VariantFailed += _ => settled.Set();
            set.VariantReady += _ => settled.Set();

            Assert.True(set.Select(2));
            Assert.True(settled.Wait(TimeSpan.FromSeconds(10)), "the frame never settled");

            Assert.Equal("Frame 3 could not be decoded (InvalidInput)", set.FailureOf(2)?.Detail);
            Assert.Null(set.FailureOf(1));
        });
    }

    [Fact]
    public void AFrameWithNoImageData_IsNamed()
    {
        var webp = new WebpBuilder(Width, Height).AddFull(Red).AddFull(WebpBuilder.Chunk("ALPH", [0])).AddFull(Blue).Build();

        var (message, _) = Failure(webp);

        Assert.Equal("Frame 2 has no image data, which makes the whole animation unreadable", message);
    }

    [Fact]
    public void AnAnimationWithoutAnim_SaysSo()
    {
        var (message, _) = Failure(new WebpBuilder(Width, Height) { LoopCount = null }.AddFull(Red).AddFull(Blue).Build());

        Assert.Equal("The animation has no ANIM chunk, which it requires", message);
    }

    [Fact]
    public void AWebpCutInsideItsHeader_SaysSo()
    {
        var (message, _) = Failure(new WebpBuilder(Width, Height).AddFull(Red).Build()[..WebpChunkReader.HeaderLength]);

        Assert.Equal("The WebP ends before its first frame", message);
    }

    [Fact]
    public void AWebpWithNoFrames_SaysSo()
    {
        var (message, facts) = Failure(new WebpBuilder(Width, Height).Build());

        Assert.Equal("The WebP holds no frames", message);
        Assert.Equal("none", facts["Frames"]);
    }

    private static Dictionary<string, string> Facts(Composite composite) => composite.FormatSpecificSnapshot().ToDictionary(p => p.Key, p => p.Value);

    private static (string Message, Dictionary<string, string> Facts) Failure(byte[] webp)
    {
        using var file = new TempFile(webp);
        using var composite = new Composite(new FileInfo(file.Path));

        var thrown = Assert.Throws<LoadFailureException>(() => new WebpDecoder().DecodeAsync(composite, CancellationToken.None).GetAwaiter().GetResult());

        return (thrown.Message, Facts(composite));
    }

    private static void WithDecoded(byte[] webp, Action<Composite> assert)
    {
        using var file = new TempFile(webp);
        using var composite = new Composite(new FileInfo(file.Path));

        new WebpDecoder().DecodeAsync(composite, CancellationToken.None).GetAwaiter().GetResult();

        assert(composite);
    }

    private static AnimationFrameRenderer Renderer(SKCodec codec) =>
        new(new SKImageInfo(codec.Info.Width, codec.Info.Height, SKColorType.Rgba8888, SKAlphaType.Premul, SKColorSpace.CreateSrgb()));

    private static uint[] Render(byte[] webp, int frame, params (int X, int Y)[] points)
    {
        using var stream = new MemoryStream(webp);
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
