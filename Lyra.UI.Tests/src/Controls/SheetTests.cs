using Lyra.UI.Components.Controls;
using SkiaSharp;
using Xunit;

namespace Lyra.UI.Tests.Controls;

/// <summary>
/// A sheet lays its tiles out in a square grid at a size fixed by its metrics alone, and hands
/// each image to its owner fitted to its cell with its shape kept; a tile without one is not drawn.
/// </summary>
public class SheetTests
{
    private static readonly SheetMetrics Metrics = new(CellSize: 100, Gap: 10, CaptionHeight: 20);

    [Theory]
    [InlineData(1, 1, 1)]
    [InlineData(3, 2, 2)]
    [InlineData(5, 3, 2)]
    public void TheSizeIsTheCellsAndGapsAlone(int count, int columns, int rows)
    {
        using var sheet = new Sheet([.. Enumerable.Range(0, count).Select(i => new SheetTile($"{i}", 0, 0, "none"))], columns, Metrics);

        var size = sheet.Layout();

        Assert.Equal(columns * 100 + (columns + 1) * 10, size.Width);
        Assert.Equal(rows * 120 + (rows + 1) * 10, size.Height);
    }

    [Fact]
    public void ImagesAreFittedToTheirCell_TilesWithoutOneAreNotDrawn()
    {
        using var sheet = new Sheet(
            [
                new SheetTile("wide", 400, 100, null),
                new SheetTile("missing", 0, 0, "File not found"),
                new SheetTile("tall", 50, 100, null)
            ],
            columns: 2,
            Metrics);

        sheet.Layout();

        var drawn = new Dictionary<int, SKRect>();
        sheet.DrawImage = (_, index, box) => drawn[index] = box;

        using var bitmap = new SKBitmap(400, 400);
        using var canvas = new SKCanvas(bitmap);
        sheet.Render(canvas);

        Assert.Equal([0, 2], drawn.Keys.Order());

        // First cell's box is 10..110 square: 400 x 100 fits as 100 x 25, centred vertically.
        Assert.Equal(SKRect.Create(10, 47.5f, 100, 25), drawn[0]);

        // Second row, first column: 50 x 100 fits as 50 x 100, centred horizontally.
        Assert.Equal(SKRect.Create(35, 140, 50, 100), drawn[2]);
    }
}