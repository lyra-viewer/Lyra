using System.Runtime.InteropServices;
using Lyra.Common.Settings.Enums;
using Lyra.Imaging.Content;
using Lyra.Imaging.Content.Tiling;
using Lyra.Imaging.Decoding.Support;
using Lyra.Renderer.Drawing;
using SkiaSharp;
using Xunit;

namespace Lyra.Core.Tests.Rendering;

[CollectionDefinition(nameof(HdrDecodeSettingsCollection), DisableParallelization = true)]
public class HdrDecodeSettingsCollection;

/// <summary>
/// An image too large for half-float tiles keeps them as compressed light. Those tiles must answer
/// the tone curve and the exposure slider exactly as the half-float preview drawn over them does.
/// </summary>
[Collection(nameof(HdrDecodeSettingsCollection))]
public class CompressedTileToneMapTests : IDisposable
{
    // Shadows, mid grey, white, highlights, a sun, and a blue sky.
    private static readonly (float R, float G, float B)[] Light =
    [
        (0.01f, 0.01f, 0.01f), (0.05f, 0.05f, 0.05f), (0.18f, 0.18f, 0.18f), (0.5f, 0.5f, 0.5f),
        (1f, 1f, 1f), (2f, 2f, 2f), (4f, 4f, 4f), (30f, 30f, 30f),
        (0.08f, 0.14f, 0.24f), (0.18f, 0.31f, 0.48f), (0.9f, 0.4f, 0.1f), (0f, 0f, 0f)
    ];

    private const float WhitePoint = 1.5f;

    private readonly ToneMapMode _mode = HdrDecodeSettings.ToneMapMode;
    private readonly int _stops = HdrDecodeSettings.ExposureStops;

    public void Dispose()
    {
        HdrDecodeSettings.ToneMapMode = _mode;
        HdrDecodeSettings.ExposureStops = _stops;
    }

    [Theory]
    [InlineData(ToneMapMode.Aces, -3)]
    [InlineData(ToneMapMode.Aces, 0)]
    [InlineData(ToneMapMode.Aces, 3)]
    [InlineData(ToneMapMode.ReinhardExtended, -2)]
    [InlineData(ToneMapMode.ReinhardExtended, 0)]
    [InlineData(ToneMapMode.ReinhardExtended, 2)]
    [InlineData(ToneMapMode.Clip, -1)]
    [InlineData(ToneMapMode.Clip, 0)]
    [InlineData(ToneMapMode.Clip, 1)]
    public void TilesMatchThePreview(ToneMapMode mode, int stops)
    {
        HdrDecodeSettings.ToneMapMode = mode;
        HdrDecodeSettings.ExposureStops = stops;

        var preview = Draw(PreviewOnly());
        var tiles = Draw(TilesOnly());

        for (var i = 0; i < Light.Length; i++)
        {
            Assert.InRange(tiles[i].Red - preview[i].Red, -2, 2);
            Assert.InRange(tiles[i].Green - preview[i].Green, -2, 2);
            Assert.InRange(tiles[i].Blue - preview[i].Blue, -2, 2);
        }
    }

    [Fact]
    public void TheExposureSliderReachesTheTiles()
    {
        HdrDecodeSettings.ToneMapMode = ToneMapMode.Aces;

        HdrDecodeSettings.ExposureStops = 0;
        var atZero = Draw(TilesOnly());

        HdrDecodeSettings.ExposureStops = 2;
        var brighter = Draw(TilesOnly());

        HdrDecodeSettings.ExposureStops = -2;
        var darker = Draw(TilesOnly());

        const int midGrey = 2;
        Assert.True(brighter[midGrey].Red > atZero[midGrey].Red + 20, $"+2 stops left mid grey at {brighter[midGrey].Red} from {atZero[midGrey].Red}.");
        Assert.True(darker[midGrey].Red < atZero[midGrey].Red - 20, $"-2 stops left mid grey at {darker[midGrey].Red} from {atZero[midGrey].Red}.");
    }

    [Fact]
    public void TheToneCurveReachesTheTiles()
    {
        HdrDecodeSettings.ExposureStops = 0;

        HdrDecodeSettings.ToneMapMode = ToneMapMode.Aces;
        var aces = Draw(TilesOnly());

        HdrDecodeSettings.ToneMapMode = ToneMapMode.Clip;
        var clip = Draw(TilesOnly());

        const int white = 4;
        Assert.Equal(255, clip[white].Red);
        Assert.True(aces[white].Red < 245, $"ACES should hold scene white below display white, drew {aces[white].Red}.");
    }

    private static RasterLargeContent PreviewOnly()
    {
        var content = new RasterLargeContent(Light.Length, 1);
        content.SetScenePreview(HalfFloat(), WhitePoint);
        return content;
    }

    private static RasterLargeContent TilesOnly()
    {
        var content = new RasterLargeContent(Light.Length, 1);

        var tiles = new RasterTileSource(1, 1, Light.Length, 1);
        tiles.SetTile(0, 0, Compressed());
        content.SetTiles(tiles);
        content.MarkAllTilesReady(1);
        content.MarkSceneReferred(WhitePoint, SceneEncoding.Compressed);

        return content;
    }

    private static SKColor[] Draw(RasterLargeContent content)
    {
        using (content)
        {
            using var composite = new Composite(new FileInfo("huge.exr"));
            composite.Content = content;
            composite.FullWidth = Light.Length;
            composite.FullHeight = 1;

            var info = new SKImageInfo(Light.Length, 1, SKColorType.Rgba8888, SKAlphaType.Unpremul, SKColorSpace.CreateSrgb());
            using var surface = SKSurface.Create(info);
            surface.Canvas.Clear(SKColors.Black);

            new SkiaCompositeContentDrawer().Draw(
                surface.Canvas,
                composite,
                destFullRect: SKRect.Create(0, 0, Light.Length, 1),
                visibleFullRect: SKRect.Create(0, 0, Light.Length, 1),
                sampling: new SKSamplingOptions(SKFilterMode.Nearest, SKMipmapMode.None),
                zoomScale: 1f,
                displayScale: 1f,
                surface: SurfaceProfile.DisplayReferred(SKColorSpace.CreateSrgb())
            );

            using var snapshot = surface.Snapshot();
            using var readback = new SKBitmap(info);
            Assert.True(snapshot.ReadPixels(readback.Info, readback.GetPixels(), readback.RowBytes, 0, 0));

            return Enumerable.Range(0, Light.Length).Select(x => readback.GetPixel(x, 0)).ToArray();
        }
    }

    private static SKImage HalfFloat()
    {
        var pixels = new Half[Light.Length * 4];
        for (var x = 0; x < Light.Length; x++)
        {
            pixels[x * 4] = (Half)Light[x].R;
            pixels[x * 4 + 1] = (Half)Light[x].G;
            pixels[x * 4 + 2] = (Half)Light[x].B;
            pixels[x * 4 + 3] = (Half)1f;
        }

        return Image(SKColorType.RgbaF16, SKColorSpace.CreateSrgbLinear(), MemoryMarshal.AsBytes(pixels.AsSpan()).ToArray());
    }

    private static SKImage Compressed()
    {
        var pixels = new byte[Light.Length * 4];
        for (var x = 0; x < Light.Length; x++)
        {
            pixels[x * 4] = CompressedLight.Encode(Light[x].R);
            pixels[x * 4 + 1] = CompressedLight.Encode(Light[x].G);
            pixels[x * 4 + 2] = CompressedLight.Encode(Light[x].B);
            pixels[x * 4 + 3] = 255;
        }

        return Image(SKColorType.Rgba8888, SKColorSpace.CreateSrgb(), pixels);
    }

    private static SKImage Image(SKColorType colorType, SKColorSpace colorSpace, byte[] pixels)
    {
        var bitmap = new SKBitmap(new SKImageInfo(Light.Length, 1, colorType, SKAlphaType.Unpremul, colorSpace));
        Marshal.Copy(pixels, 0, bitmap.GetPixels(), pixels.Length);
        bitmap.SetImmutable();
        return SKImage.FromBitmap(bitmap);
    }
}
