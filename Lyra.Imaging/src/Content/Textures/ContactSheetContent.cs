using System.Diagnostics;
using SkiaSharp;

namespace Lyra.Imaging.Content.Textures;

public sealed record SheetCell(string Caption, ICompositeContent? Content, string? Failure);

/// <summary>
/// Several textures side by side in a grid of square cells, each captioned. Cells hold reduced
/// copies, so a sheet of large textures stays small; HDR cells stay HDR and are tone-mapped when drawn.
/// Holds what the sheet shows and its size, known before any cell is decoded; laying it out and
/// drawing it is the UI's <c>Sheet</c>, built from the same constants.
/// </summary>
[DebuggerDisplay("{Cells.Count} cells, {Columns} x {Rows}")]
public sealed class ContactSheetContent : ICompositeContent
{
    public const int CellSize = 1024;
    public const int Gap = 48;
    public const int CaptionHeight = 112;

    public ContactSheetContent(IReadOnlyList<SheetCell> cells)
    {
        ArgumentNullException.ThrowIfNull(cells);

        Cells = cells;
        Columns = ColumnsFor(cells.Count);
        Rows = cells.Count == 0 ? 0 : RowsFor(cells.Count, Columns);
        _size = SizeFor(cells.Count);
    }

    private readonly SKSizeI _size;

    public IReadOnlyList<SheetCell> Cells { get; }

    public int Columns { get; }

    public int Rows { get; }

    public bool IsResolutionIndependent => false;

    public float? DecodedWidth => _size.Width;

    public float? DecodedHeight => _size.Height;

    public long ByteSize => Cells.Sum(cell => cell.Content?.ByteSize ?? 0);

    public bool HasHdrCells => Cells.Any(cell => cell.Content is HdrRasterContent);

    /// <summary>The sheet's size for <paramref name="count"/> cells, known before any is decoded.</summary>
    public static SKSizeI SizeFor(int count) => count <= 0 ? new SKSizeI(CellSize, CellSize) : SizeFor(count, ColumnsFor(count));

    private static SKSizeI SizeFor(int count, int columns)
    {
        var rows = RowsFor(count, columns);
        return new SKSizeI(columns * CellSize + (columns + 1) * Gap, rows * (CellSize + CaptionHeight) + (rows + 1) * Gap);
    }

    private static int RowsFor(int count, int columns) => (count + columns - 1) / columns;

    /// <summary>The landscape view a sheet is laid out to fill, as width over height.</summary>
    private const float ViewAspect = 16f / 10f;

    /// <summary>The columns that make the cells largest in a landscape view; of equals, the fewest.</summary>
    internal static int ColumnsFor(int count)
    {
        var best = 1;
        var bestScale = 0f;

        for (var columns = 1; columns <= Math.Max(1, count); columns++)
        {
            var size = SizeFor(count, columns);
            var scale = Math.Min(ViewAspect / size.Width, 1f / size.Height);

            if (scale > bestScale * 1.001f)
            {
                best = columns;
                bestScale = scale;
            }
        }

        return best;
    }

    public void Dispose()
    {
        foreach (var cell in Cells)
            cell.Content?.Dispose();
    }
}