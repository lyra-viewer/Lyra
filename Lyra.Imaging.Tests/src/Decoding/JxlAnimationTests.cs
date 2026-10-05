using System.Runtime.InteropServices;
using Lyra.Imaging.Content;
using Lyra.Imaging.Decoding.Decoders;
using Lyra.Imaging.Interop;
using Lyra.Imaging.Tests.Support;
using SkiaSharp;
using Xunit;

namespace Lyra.Imaging.Tests.Decoding;

/// <summary>
/// Animated JPEG XL through the native wrapper. The fixtures are tiny real files, lossless so
/// pixels can be checked exactly; like <see cref="JxlColorSpaceTests"/>, these skip when the
/// native wrapper has not been built for this platform.
/// </summary>
public class JxlAnimationTests
{
    // 16x12, three frames: red 100 ms; a 4x4 green layer at 2,4 for 200 ms; blue 50 ms (cjxl -d 0 of an APNG).
    private const string ThreeFrames =
        "/wpYMMEA1gQIACCZAABEAEsYixWAXep/Aul2Aswj/f0BCIAQgAABBEQiCwBIAEsYixXCSUEOAEB/AAAAQH8AAAgAoEwAAEQASxiLFYBd6n+C/nYC7pv8/QE=";

    // 16x12, two frames: the left half clear and the right red, then blue.
    private const string ClearThenBlue =
        "/wpYMMEA1gQIACCZAAB0AEsYixUA1LiLwegFRADIpSQK8Jel69/2dwgvA7YCCAAgWQAARABLGIsVgF3qf4L+dgLum/z9AQ==";

    // 4x4, red then blue, 7 ticks each at 100 ticks a second, three plays in all (libjxl's encoder).
    private const string ThreePlays =
        "/woYEEFoEBMIAB4AADwASxiLFcJJQU5/AAAAAAAACAAeBAA8AEsYixXCSUEOAAAAQH8AAA==";

    // 8x4 linear float: (4, 0.25, 0.25) for 120 ms, then (0.25, 0.25, 2) for 80 ms.
    private const string HdrFrames =
        "AAAADEpYTCANCocKAAAAFGZ0eXBqeGwgAAAAAGp4bCAAAAAJanhsbAoAAABEanhscAAAAAD/ChhwwUBOyAFQtBIIACAeAACQAEsYk46DhQhIRwxmllOBKQBAJQ0A" +
        "ABAAAADoARIBAAB6AACAHwAAADlqeGxwgAAAAQgAIFQAAJQASxib7U5BQQSkIwYzy+G/FACglwAAAOgBAAB6gEQAAAAAAADADw==";

    // ThreeFrames again, lossy (cjxl -d 1): XYB frames that build on earlier ones, which libjxl 0.12
    // fails after the first when asked to convert them to any color space but the file's own.
    private const string LossyThreeFrames =
        "/wpYMMEA9gQATADJBAUANAG1nyAAABUqo4wbvJzr+fJDh8W0jesMbbVtYQljsz0XCgYJAXABdboY491NIwWQhz9Mf/NYbwGAEsESSCAyAGEDIAAMgOoaBgAg" +
        "4FaSAABMhAAECCAgElkoANgAtZ8gAAAVKqOMG7yc6/nyQ4fFtI3rDG21bWEJY7M9FwoGCQlnqF7YoT75AwCap7yn7oZxVEkCAEwAZaIAABwBtZ8gAAAVKqOM" +
        "G7yc6/nyQ4fFtI3rDG21bWEJY7M9FwoGCQFwAGNkjDEeJ5DaAQAaxr+pqAsAINqnBwESjul4LAAAkAUqSQA=";

    private static readonly SKColor Red = new(255, 0, 0), Green = new(0, 255, 0), Blue = new(0, 0, 255);

    private static readonly Lazy<bool> NativeJxlReady = new(() => NativeWrapper.TryLoad("libjxl_native", typeof(JxlDecoder).Assembly));

    [Fact]
    public void AnAnimation_IsPublishedAsFrames()
    {
        WithDecoded(ThreeFrames, composite =>
        {
            var set = Assert.IsType<VariantRasterContent>(composite.Content);

            Assert.Equal(VariantKind.Frames, set.Kind);
            Assert.Equal(["Frame 1", "Frame 2", "Frame 3"], set.Variants.Select(v => v.Label));
            Assert.Equal(["16x12, 100 ms", "16x12, 200 ms", "16x12, 50 ms"], set.Variants.Select(v => v.Detail));
            Assert.All(set.Variants, v => Assert.Null(v.ByteSize));

            var facts = Facts(composite);
            Assert.Equal("3", facts["Frames"]);
            Assert.Equal("350 ms", facts["Duration"]);
            Assert.Equal("forever", facts["Loop"]);
            Assert.Equal("No", facts["HDR"]);
            Assert.False(facts.ContainsKey("Animated"));
            Assert.False(facts.ContainsKey("Truncated"));

            // Whether pixels are gray is a frame's answer, not the file's.
            Assert.False(facts.ContainsKey("GrayScale"));

            AssertSrgb(Red, set.Active, 0, 0);
        });
    }

    [Fact]
    public void ALayer_IsCompositedOntoTheFrameBelow()
    {
        WithDecoded(ThreeFrames, composite =>
        {
            var set = Assert.IsType<VariantRasterContent>(composite.Content);

            Assert.True(SelectAndWait(set, 1));
            AssertSrgb(Green, set.Active, 3, 5);
            AssertSrgb(Red, set.Active, 0, 0);
            AssertSrgb(Red, set.Active, 15, 11);

            Assert.True(SelectAndWait(set, 2));
            AssertSrgb(Blue, set.Active, 3, 5);
        });
    }

    [Fact]
    public void ALossyAnimation_DecodesEveryFrame_InItsOwnColorSpace()
    {
        WithDecoded(LossyThreeFrames, composite =>
        {
            var set = Assert.IsType<VariantRasterContent>(composite.Content);
            AssertSrgb(Red, set.Active, 12, 8, tolerance: 32);

            Assert.True(SelectAndWait(set, 1));
            Assert.Null(set.FailureOf(1));
            AssertSrgb(Green, set.Active, 3, 5, tolerance: 32);
            AssertSrgb(Red, set.Active, 12, 8, tolerance: 32);

            Assert.True(SelectAndWait(set, 2));
            Assert.Null(set.FailureOf(2));
            AssertSrgb(Blue, set.Active, 3, 5, tolerance: 32);

            // Tagged with the file's sRGB profile rather than converted to Display-P3.
            var raster = Assert.IsType<RasterContent>(set.Active);
            Assert.True(raster.Image.ColorSpace is { IsSrgb: true } || !raster.Image.ColorSpace!.GammaIsLinear);
            Assert.False(SKColorSpace.Equal(raster.Image.ColorSpace, SKColorSpace.CreateRgb(SKColorSpaceTransferFn.Srgb, SKColorSpaceXyz.DisplayP3)));
        });
    }

    [Fact]
    public void AClearArea_StaysClear()
    {
        WithDecoded(ClearThenBlue, composite =>
        {
            var set = Assert.IsType<VariantRasterContent>(composite.Content);

            Assert.Equal(0, PixelOf(set.Active, 2, 2).Alpha);
            AssertSrgb(Red, set.Active, 12, 2);
            Assert.Equal("Yes", Facts(composite)["Has Alpha"]);
        });
    }

    [Fact]
    public void TheLoopCount_AndTickDurations_AreRead()
    {
        WithDecoded(ThreePlays, composite =>
        {
            var set = Assert.IsType<VariantRasterContent>(composite.Content);

            Assert.Equal(["4x4, 70 ms", "4x4, 70 ms"], set.Variants.Select(v => v.Detail));
            Assert.Equal("2 repeats", Facts(composite)["Loop"]);
        });
    }

    [Fact]
    public void AnHdrAnimation_KeepsEveryFrameSceneReferred()
    {
        WithDecoded(HdrFrames, composite =>
        {
            var set = Assert.IsType<VariantRasterContent>(composite.Content);
            Assert.Equal("Yes", Facts(composite)["HDR"]);

            // Each frame has its own range, which is not the file's.
            Assert.False(Facts(composite).ContainsKey("Dynamic Range"));

            var first = PixelOf(Assert.IsType<HdrRasterContent>(set.Active), 1, 1);
            Assert.True(first.Red > first.Blue);

            Assert.True(SelectAndWait(set, 1));
            var second = PixelOf(Assert.IsType<HdrRasterContent>(set.Active), 1, 1);
            Assert.True(second.Blue > second.Red);
        });
    }

    [Fact]
    public void AnyOrder_DecodesTheSameFramesAsAColdStart()
    {
        if (!NativeJxlReady.Value)
            Assert.Skip("libjxl_native not available (native wrappers not built for this platform).");

        var data = Convert.FromBase64String(ThreeFrames);

        var cold = new byte[3][];
        for (var i = 0; i < cold.Length; i++)
        {
            using var handle = Open(data, out _);
            cold[i] = Frame(handle, i);
        }

        using var warm = Open(data, out _);
        foreach (var i in new[] { 0, 1, 2, 2, 0, 2, 1, 1, 0 })
            Assert.True(cold[i].SequenceEqual(Frame(warm, i)), $"frame {i} differs when decoded warm");
    }

    [Fact]
    public void AFileCutInsideTheLastFrame_FailsThatFrameAlone()
    {
        WithDecoded(ThreeFrames, cut: 80, composite =>
        {
            var set = Assert.IsType<VariantRasterContent>(composite.Content);

            Assert.True(SelectAndWait(set, 1));
            Assert.Null(set.FailureOf(1));

            Assert.False(SelectAndWait(set, 2));
            Assert.Equal("Frame 3 is cut short: the file ends inside it", set.FailureOf(2)?.Detail);
        });
    }

    [Fact]
    public void AFileCutBeforeAFrame_ListsTheFramesBeforeIt()
    {
        WithDecoded(ThreeFrames, cut: 70, composite =>
        {
            var set = Assert.IsType<VariantRasterContent>(composite.Content);

            Assert.Equal(2, set.Variants.Count);
            Assert.Equal("stops before the animation ends", Facts(composite)["Truncated"]);
        });
    }

    [Fact]
    public void AStillImage_KeepsTheStillPath()
    {
        WithDecoded(JxlColorSpaceTests.DisplayP3JxlBase64, composite =>
        {
            Assert.IsType<RasterContent>(composite.Content);

            var facts = Facts(composite);
            Assert.Equal("No", facts["Animated"]);
            Assert.Equal("No", facts["GrayScale"]);
            Assert.False(facts.ContainsKey("Frames"));
        });
    }

    [Fact]
    public void AStillImage_HasNoFramesToDecode()
    {
        if (!NativeJxlReady.Value)
            Assert.Skip("libjxl_native not available (native wrappers not built for this platform).");

        using var handle = Open(Convert.FromBase64String(JxlColorSpaceTests.DisplayP3JxlBase64), out var info);

        Assert.Equal(1, info.FrameCount);
        Assert.False(JxlNative.jxl_animation_decode_frame(handle, 0, out _, out _));
    }

    private static void WithDecoded(string base64, Action<Composite> assert) => WithDecoded(base64, cut: null, assert);

    private static void WithDecoded(string base64, int? cut, Action<Composite> assert)
    {
        if (!NativeJxlReady.Value)
            Assert.Skip("libjxl_native not available (native wrappers not built for this platform).");

        var data = Convert.FromBase64String(base64);
        using var file = new TempFile(cut is { } length ? data[..length] : data);
        using var composite = new Composite(new FileInfo(file.Path));

        new JxlDecoder().DecodeAsync(composite, CancellationToken.None).GetAwaiter().GetResult();

        assert(composite);
    }

    private static unsafe JxlNative.AnimationHandle Open(byte[] data, out JxlNative.AnimationInfo info)
    {
        fixed (byte* p = data)
            return JxlNative.OpenAnimation((IntPtr)p, (nuint)data.Length, out info) ?? throw new InvalidOperationException("The fixture did not open.");
    }

    private static byte[] Frame(JxlNative.AnimationHandle handle, int index)
    {
        Assert.True(JxlNative.jxl_animation_decode_frame(handle, index, out var pixels, out var partial));
        Assert.Equal(0, partial);

        try
        {
            var bytes = new byte[16 * 12 * 4];
            Marshal.Copy(pixels, bytes, 0, bytes.Length);
            return bytes;
        }
        finally
        {
            JxlNative.free_jxl_pixels(pixels);
        }
    }

    private static Dictionary<string, string> Facts(Composite composite) => composite.FormatSpecificSnapshot().ToDictionary(p => p.Key, p => p.Value);

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

    private static SKColor PixelOf(ICompositeContent? content, int x, int y)
    {
        var raster = Assert.IsAssignableFrom<RasterContent>(content);
        using var bitmap = SKBitmap.FromImage(raster.Image);
        return bitmap.GetPixel(x, y);
    }

    /// <summary>
    /// The pixel converted to sRGB, since SDR frames arrive in Display-P3; the round trip through
    /// eight-bit P3 moves a channel by a few levels.
    /// </summary>
    private static void AssertSrgb(SKColor expected, ICompositeContent? content, int x, int y, int tolerance = 4)
    {
        var raster = Assert.IsAssignableFrom<RasterContent>(content);
        var info = new SKImageInfo(1, 1, SKColorType.Rgba8888, SKAlphaType.Unpremul, SKColorSpace.CreateSrgb());

        using var pixel = new SKBitmap(info);
        Assert.True(raster.Image.ReadPixels(info, pixel.GetPixels(), info.RowBytes, x, y));

        var actual = pixel.GetPixel(0, 0);
        Assert.True(Math.Abs(actual.Red - expected.Red) <= tolerance && Math.Abs(actual.Green - expected.Green) <= tolerance && Math.Abs(actual.Blue - expected.Blue) <= tolerance, $"({x},{y}) is {actual}, expected {expected}");
    }
}