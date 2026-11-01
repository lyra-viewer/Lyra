using Lyra.Imaging.Content;
using Lyra.Imaging.Content.Hdr;
using Lyra.Imaging.Content.Textures;
using Lyra.Imaging.Decoding.Support;
using SkiaSharp;
using Xunit;

namespace Lyra.Imaging.Tests.Content;

public class ContactSheetTests
{
    private static SKBitmap Bitmap(int width, int height, SKColorType colorType)
    {
        var bitmap = new SKBitmap(new SKImageInfo(width, height, colorType, SKAlphaType.Premul));
        bitmap.Erase(SKColors.Teal);
        return bitmap;
    }

    private static RasterContent Raster(int width, int height)
    {
        var bitmap = Bitmap(width, height, SKColorType.Rgba8888);
        return new RasterContent(bitmap, SKImage.FromBitmap(bitmap));
    }

    [Theory]
    [InlineData(1, 1, 1)]
    [InlineData(2, 2, 1)]
    [InlineData(3, 3, 1)]
    [InlineData(4, 2, 2)]
    [InlineData(5, 3, 2)]
    [InlineData(7, 4, 2)]
    [InlineData(9, 5, 2)]
    public void CellsAreLaidOutToFillALandscapeView(int count, int columns, int rows)
    {
        using var sheet = new ContactSheetContent([.. Enumerable.Range(0, count).Select(i => new SheetCell($"{i}", null, "none"))]);

        Assert.Equal((columns, rows), (sheet.Columns, sheet.Rows));

        var size = ContactSheetContent.SizeFor(count);
        Assert.Equal((size.Width, size.Height), ((int)sheet.DecodedWidth!.Value, (int)sheet.DecodedHeight!.Value));
    }

    [Fact]
    public void ALargeTextureIsReducedToTheCellAndKeepsItsShape()
    {
        using var source = Raster(4096, 1024);
        using var copy = ContentThumbnail.Make(source, ContactSheetContent.CellSize);

        var raster = Assert.IsType<RasterContent>(copy);
        Assert.Equal((1024f, 256f), (raster.DecodedWidth!.Value, raster.DecodedHeight!.Value));
    }

    [Fact]
    public void ASmallTextureIsCopiedAtItsOwnSizeAndOutlivesTheOriginal()
    {
        ICompositeContent? copy;
        using (var source = Raster(12, 6))
            copy = ContentThumbnail.Make(source, ContactSheetContent.CellSize);

        using (copy)
        {
            var raster = Assert.IsType<RasterContent>(copy);
            using var bitmap = SKBitmap.FromImage(raster.Image);
            var pixel = (uint)bitmap.GetPixel(3, 3);

            Assert.Equal((12, 6), (bitmap.Width, bitmap.Height));
            Assert.Equal((uint)SKColors.Teal, pixel);
        }
    }

    [Fact]
    public void AnHdrTextureStaysSceneReferred()
    {
        var bitmap = Bitmap(2048, 2048, SKColorType.RgbaF16);
        using var source = new HdrRasterContent(bitmap, SKImage.FromBitmap(bitmap), whitePoint: 6.5f, SceneEncoding.Linear);

        using var copy = ContentThumbnail.Make(source, ContactSheetContent.CellSize);

        var hdr = Assert.IsType<HdrRasterContent>(copy);
        Assert.Equal((1024f, 1024f, 6.5f), (hdr.DecodedWidth!.Value, hdr.DecodedHeight!.Value, hdr.WhitePoint));
        Assert.Equal(SKColorType.RgbaF16, hdr.Image.ColorType);
    }

    [Fact]
    public void ALargeRasterGivesItsPreview()
    {
        var preview = Bitmap(800, 400, SKColorType.Rgba8888);
        using var large = new RasterLargeContent(16000, 8000, SKImage.FromBitmap(preview));

        using var copy = ContentThumbnail.Make(large, ContactSheetContent.CellSize);

        var raster = Assert.IsType<RasterContent>(copy);
        Assert.Equal((800f, 400f), (raster.DecodedWidth!.Value, raster.DecodedHeight!.Value));
    }

    [Fact]
    public void ContentWithoutPixelsHasNoCopy()
    {
        using var empty = new RasterLargeContent(100, 100);

        Assert.Null(ContentThumbnail.Make(empty, ContactSheetContent.CellSize));
    }
}