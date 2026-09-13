using Lyra.Imaging.Content.Tiling;
using Lyra.Renderer.Drawing;
using SkiaSharp;
using Xunit;

namespace Lyra.Core.Tests.Rendering;

/// <summary>
/// The guard that stops the renderer asking the GPU for more tiles than its cache can hold.
/// </summary>
public class TileTextureBudgetTests
{
    private const long Budget = 256L * 1024 * 1024;

    [Fact]
    public void FitsWhenUnderBudget()
    {
        Assert.True(SkiaCompositeContentDrawer.TilesFitBudget(200L * 1024 * 1024, mipmapped: false, Budget));
    }

    [Fact]
    public void DoesNotFitWhenOverBudget()
    {
        Assert.False(SkiaCompositeContentDrawer.TilesFitBudget(300L * 1024 * 1024, mipmapped: false, Budget));
    }
    
    [Fact]
    public void MipmapsCountTowardTheBudget()
    {
        var bytes = 200L * 1024 * 1024;

        Assert.True(SkiaCompositeContentDrawer.TilesFitBudget(bytes, mipmapped: false, Budget));
        Assert.False(SkiaCompositeContentDrawer.TilesFitBudget(bytes, mipmapped: true, Budget));
    }

    [Fact]
    public void TheBudgetIsInclusive()
    {
        Assert.True(SkiaCompositeContentDrawer.TilesFitBudget(Budget, mipmapped: false, Budget));
        Assert.False(SkiaCompositeContentDrawer.TilesFitBudget(Budget + 1, mipmapped: false, Budget));
    }
    
    [Fact]
    public void NothingDecodedYetFits()
    {
        Assert.True(SkiaCompositeContentDrawer.TilesFitBudget(0, mipmapped: true, Budget));
    }
    
    [Fact]
    public void VisibleByteSizeCountsOnlyTheTilesThatOverlap()
    {
        using var tiles = new RasterTileSource(tilesX: 4, tilesY: 4, tileWidth: 64, tileHeight: 64);

        for (var y = 0; y < 4; y++)
        for (var x = 0; x < 4; x++)
            tiles.SetTile(x, y, Tile(64));

        var full = new SKSize(256, 256);
        const long oneTile = 64L * 64 * 4;

        Assert.Equal(oneTile, tiles.VisibleByteSize(SKRect.Create(0, 0, 1, 1), full, 1f));
        Assert.Equal(oneTile * 4, tiles.VisibleByteSize(SKRect.Create(0, 0, 128, 128), full, 1f));
        Assert.Equal(oneTile * 16, tiles.VisibleByteSize(SKRect.Create(0, 0, 256, 256), full, 1f));
    }

    [Fact]
    public void UndecodedTilesCostNothing()
    {
        using var tiles = new RasterTileSource(tilesX: 2, tilesY: 2, tileWidth: 64, tileHeight: 64);
        tiles.SetTile(0, 0, Tile(64));

        Assert.Equal(64L * 64 * 4, tiles.VisibleByteSize(SKRect.Create(0, 0, 128, 128), new SKSize(128, 128), 1f));
    }

    private static SKImage Tile(int edge)
    {
        var info = new SKImageInfo(edge, edge, SKColorType.Rgba8888, SKAlphaType.Premul);
        var bitmap = new SKBitmap(info);
        bitmap.Erase(SKColors.Gray);
        bitmap.SetImmutable();

        return SKImage.FromBitmap(bitmap);
    }
}