using Lyra.Imaging.Content;
using Lyra.Imaging.Decoding.Support;
using SkiaSharp;
using Xunit;

namespace Lyra.Imaging.Tests.Decoding;

public class RasterContentBuilderTests
{
    // 64 MP is the budget, so 8192x8192 sits exactly on it and stays a single texture.
    private const int AtBudgetEdge = 8192;

    [Fact]
    public void SmallImage_StaysASingleTexture()
    {
        using var bitmap = Filled(64, 64, SKColors.CornflowerBlue);
        var composite = NewComposite();

        using var content = RasterContentBuilder.Build(bitmap, composite);

        Assert.IsType<RasterContent>(content);
    }

    [Fact]
    public void SmallImage_LeavesTheCompositeToReportItsOwnSize()
    {
        using var bitmap = Filled(64, 32, SKColors.CornflowerBlue);
        var composite = NewComposite();

        using var content = RasterContentBuilder.Build(bitmap, composite);

        Assert.Null(composite.FullWidth);
        Assert.Null(composite.FullHeight);
        Assert.Equal(64f, content.DecodedWidth);
        Assert.Equal(32f, content.DecodedHeight);
    }

    [Fact]
    public void ExactlyAtBudget_StaysASingleTexture()
    {
        using var bitmap = Filled(AtBudgetEdge, AtBudgetEdge, SKColors.DimGray);
        var composite = NewComposite();

        using var content = RasterContentBuilder.Build(bitmap, composite);

        Assert.IsType<RasterContent>(content);
    }

    [Fact]
    public void OverBudget_BecomesPreviewPlusTiles()
    {
        using var bitmap = Filled(AtBudgetEdge, AtBudgetEdge + 1, SKColors.DimGray);
        var composite = NewComposite();

        using var content = RasterContentBuilder.Build(bitmap, composite);

        var large = Assert.IsType<RasterLargeContent>(content);
        
        Assert.True(large.HasPreview, "a large raster must publish a preview to draw at fit-to-window");
        Assert.True(large.HasTiles, "a large raster must publish tiles so zoom stays sharp");
    }

    [Fact]
    public void OverBudget_RecordsTheRealSizeOnTheComposite()
    {
        using var bitmap = Filled(AtBudgetEdge, AtBudgetEdge + 1, SKColors.DimGray);
        var composite = NewComposite();

        using var content = RasterContentBuilder.Build(bitmap, composite);

        Assert.Equal(AtBudgetEdge, composite.FullWidth);
        Assert.Equal(AtBudgetEdge + 1, composite.FullHeight);
        Assert.Equal(AtBudgetEdge, composite.LogicalWidth);
        Assert.Equal(AtBudgetEdge + 1, composite.LogicalHeight);

        var large = (RasterLargeContent)content;
        
        Assert.True(large.PreviewImage!.Width < AtBudgetEdge, $"preview is {large.PreviewImage.Width}px wide, no smaller than the source");
    }

    [Fact]
    public void OverBudget_TilesCoverTheImageAndMapToTheRightPlace()
    {
        const int width = AtBudgetEdge;
        const int height = AtBudgetEdge + 1;

        using var bitmap = Filled(width, height, SKColors.Black);
        using (var canvas = new SKCanvas(bitmap))
        {
            using var paint = new SKPaint { Color = SKColors.Red };
            canvas.DrawRect(SKRect.Create(4096, 2048, 2048, 2048), paint);   // exactly tile (2,1)
        }

        var composite = NewComposite();
        using var content = RasterContentBuilder.Build(bitmap, composite);
        var large = (RasterLargeContent)content;

        var imageSize = new SKSize(width, height);
        var marked = Single(large.TileSource!.GetTiles(SKRect.Create(4096, 2048, 2048, 2048), imageSize));
        var plain = Single(large.TileSource!.GetTiles(SKRect.Create(0, 0, 2048, 2048), imageSize));

        Assert.Equal(SKColors.Red, CentrePixel(marked.Image));
        Assert.Equal(SKColors.Black, CentrePixel(plain.Image));
    }

    [Fact]
    public void OverBudget_OnlyVisibleTilesAreReturned()
    {
        using var bitmap = Filled(AtBudgetEdge, AtBudgetEdge + 1, SKColors.DimGray);
        var composite = NewComposite();

        using var content = RasterContentBuilder.Build(bitmap, composite);
        var large = (RasterLargeContent)content;
        var imageSize = new SKSize(AtBudgetEdge, AtBudgetEdge + 1);

        var corner = large.TileSource!.GetTiles(SKRect.Create(0, 0, 100, 100), imageSize).Count();
        var everything = large.TileSource!.GetTiles(SKRect.Create(0, 0, AtBudgetEdge, AtBudgetEdge + 1), imageSize).Count();

        Assert.Equal(1, corner);
        Assert.True(everything > corner, $"the full view returned {everything} tiles, no more than one corner");
    }

    [Fact]
    public void OverBudget_DeclaresItsTilesReady()
    {
        using var bitmap = Filled(AtBudgetEdge, AtBudgetEdge + 1, SKColors.DimGray);
        var composite = NewComposite();

        using var content = RasterContentBuilder.Build(bitmap, composite);
        var large = (RasterLargeContent)content;

        Assert.NotNull(large.TilesTotal);
        Assert.True(large.TilesTotal > 0, "a tiled image must report how many tiles it has");
        Assert.Equal(large.TilesTotal, large.TilesReady);

        var everything = large.TileSource!
            .GetTiles(SKRect.Create(0, 0, AtBudgetEdge, AtBudgetEdge + 1), new SKSize(AtBudgetEdge, AtBudgetEdge + 1))
            .Count();
        
        Assert.Equal(large.TilesTotal, everything);
    }

    [Fact]
    public void OverBudget_ReportsWhatItActuallyHolds()
    {
        const int width = AtBudgetEdge;
        const int height = AtBudgetEdge + 1;
        const long sourceBytes = (long)width * height * 4;

        using var bitmap = Filled(width, height, SKColors.DimGray);
        var composite = NewComposite();

        using var content = RasterContentBuilder.Build(bitmap, composite);
        var large = (RasterLargeContent)content;

        var previewBytes = (long)large.PreviewImage!.Width * large.PreviewImage.Height * 4;

        // The tiles are views onto the source, so the source is counted once, plus the preview.
        Assert.Equal(sourceBytes + previewBytes, content.ByteSize);

        // And the thing that was actually wrong: it is not merely the preview.
        Assert.True(content.ByteSize > previewBytes * 2,
            $"ByteSize {content.ByteSize} is barely above the preview's {previewBytes} - it is still under-reporting");
    }

    [Fact]
    public void SmallImage_ReportsItsOwnPixels()
    {
        using var bitmap = Filled(64, 32, SKColors.CornflowerBlue);
        var composite = NewComposite();

        using var content = RasterContentBuilder.Build(bitmap, composite);

        Assert.Equal(64L * 32 * 4, content.ByteSize);
    }
    
    private static SKBitmap Filled(int width, int height, SKColor color)
    {
        var bitmap = new SKBitmap(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul));
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(color);
        return bitmap;
    }

    private static Composite NewComposite() => new(new FileInfo("large.exr"));

    private static RasterTile Single(IEnumerable<RasterTile> tiles)
    {
        var list = tiles.ToList();
        Assert.Single(list);
        return list[0];
    }

    private static SKColor CentrePixel(SKImage image)
    {
        using var bitmap = SKBitmap.FromImage(image);
        return bitmap.GetPixel(bitmap.Width / 2, bitmap.Height / 2);
    }
}
