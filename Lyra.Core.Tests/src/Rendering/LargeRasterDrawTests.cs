using Lyra.Imaging.Content;
using Lyra.Renderer.Drawing;
using SkiaSharp;
using Xunit;

namespace Lyra.Core.Tests.Rendering;

public class LargeRasterDrawTests
{
    private const int FullSize = 4096;
    private const int PreviewSize = 512; // previewPpfu = 512/4096 = 0.125
    private const int TileEdge = 2048;

    // DrawRasterLarge switches when screenPpfu > previewPpfu * 1.05, so 0.13125 here.
    private const float SwitchPoint = 0.125f * 1.05f;

    [Fact]
    public void AtFitToWindow_DrawsThePreview()
    {
        Assert.Equal(SKColors.Gray, DrawAndSample(zoomScale: 0.05f, displayScale: 1f));
    }

    [Fact]
    public void WhenZoomedIn_DrawsTiles()
    {
        Assert.Equal(SKColors.Red, DrawAndSample(zoomScale: 1f, displayScale: 2f));
    }

    [Fact]
    public void TheSwitchHappensWhereTheThresholdSays()
    {
        Assert.Equal(SKColors.Gray, DrawAndSample(SwitchPoint * 0.95f, displayScale: 1f));
        Assert.Equal(SKColors.Red, DrawAndSample(SwitchPoint * 1.05f, displayScale: 1f));
    }

    [Fact]
    public void DisplayScaleCountsTowardsTheSwitch()
    {
        const float zoom = SwitchPoint * 0.6f;

        Assert.Equal(SKColors.Gray, DrawAndSample(zoom, displayScale: 1f));
        Assert.Equal(SKColors.Red, DrawAndSample(zoom, displayScale: 2f));
    }

    [Fact]
    public void WithoutTiles_ThePreviewStillDraws()
    {
        using var content = new RasterLargeContent(FullSize, FullSize);
        content.SetPreview(Solid(PreviewSize, SKColors.Gray));

        Assert.Equal(SKColors.Gray, DrawAndSample(zoomScale: 1f, displayScale: 2f, content: content));
    }
    
    private static SKColor DrawAndSample(float zoomScale, float displayScale, RasterLargeContent? content = null)
    {
        var owned = content is null;
        content ??= LargeContent();

        try
        {
            using var composite = new Composite(new FileInfo("huge.exr"));
            composite.Content = content;
            composite.FullWidth = FullSize;
            composite.FullHeight = FullSize;

            using var surface = SKSurface.Create(new SKImageInfo(64, 64, SKColorType.Rgba8888, SKAlphaType.Premul));
            var canvas = surface.Canvas;
            canvas.Clear(SKColors.Black);

            canvas.Scale(zoomScale * displayScale);

            new SkiaCompositeContentDrawer().Draw(
                canvas,
                composite,
                destFullRect: SKRect.Create(0, 0, FullSize, FullSize),
                visibleFullRect: SKRect.Create(0, 0, 64, 64),
                sampling: new SKSamplingOptions(SKFilterMode.Nearest, SKMipmapMode.None),
                zoomScale: zoomScale,
                displayScale: displayScale,
                surface: SurfaceProfile.Unknown
            );

            canvas.Flush();

            using var snapshot = surface.Snapshot();
            using var bitmap = SKBitmap.FromImage(snapshot);
            return bitmap.GetPixel(32, 32);
        }
        finally
        {
            if (owned)
                content.Dispose();
        }
    }

    private static RasterLargeContent LargeContent()
    {
        var content = new RasterLargeContent(FullSize, FullSize);
        content.SetPreview(Solid(PreviewSize, SKColors.Gray));

        var tiles = new RasterTileSource(2, 2, TileEdge, TileEdge);
        for (var y = 0; y < 2; y++)
        for (var x = 0; x < 2; x++)
            tiles.SetTile(x, y, Solid(8, SKColors.Red));

        content.SetTiles(tiles);
        content.MarkAllTilesReady(4);

        return content;
    }

    private static SKImage Solid(int size, SKColor color)
    {
        var bitmap = new SKBitmap(new SKImageInfo(size, size, SKColorType.Rgba8888, SKAlphaType.Premul));
        using (var canvas = new SKCanvas(bitmap))
            canvas.Clear(color);

        bitmap.SetImmutable();
        return SKImage.FromBitmap(bitmap);
    }
}