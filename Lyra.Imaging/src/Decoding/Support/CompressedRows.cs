using Lyra.Imaging.Content;
using SkiaSharp;

namespace Lyra.Imaging.Decoding.Support;

/// <summary>Writes linear rows into an RGBA8888 bitmap as <see cref="SceneEncoding.Compressed"/> light.</summary>
internal static class CompressedRows
{
    /// <summary>Returns true when every pixel has R == G == B.</summary>
    public static unsafe bool Write(LinearRows rows, SKBitmap bitmap, CancellationToken ct)
    {
        if (bitmap.ColorType != SKColorType.Rgba8888 || bitmap.Width != rows.Width || bitmap.Height != rows.Height)
            throw new ArgumentException("Expected an RGBA8888 bitmap the size of the rows.", nameof(bitmap));

        var width = rows.Width;
        var dst = (nint)bitmap.GetPixels();
        var dstRowBytes = bitmap.RowBytes;

        return rows.AllRows(ct, (y, row) =>
        {
            var gray = true;
            var dstRow = (byte*)dst + (nint)y * dstRowBytes;

            for (var x = 0; x < width; x++)
            {
                var i = x * 4;
                var r = row[i];
                var g = row[i + 1];
                var b = row[i + 2];

                if (g != r || b != r)
                    gray = false;

                dstRow[i] = HdrToneMap.Encode(CompressedLight.Compress(r));
                dstRow[i + 1] = HdrToneMap.Encode(CompressedLight.Compress(g));
                dstRow[i + 2] = HdrToneMap.Encode(CompressedLight.Compress(b));
                dstRow[i + 3] = HdrToneMap.AlphaToByte(row[i + 3]);
            }

            return gray;
        });
    }
}