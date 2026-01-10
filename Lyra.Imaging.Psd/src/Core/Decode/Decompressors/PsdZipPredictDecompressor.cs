using System.Buffers;
using System.IO.Compression;
using Lyra.Imaging.Psd.Core.Common;
using Lyra.Imaging.Psd.Core.Decode.Pixel;
using Lyra.Imaging.Psd.Core.Readers;
using Lyra.Imaging.Psd.Core.SectionData;

namespace Lyra.Imaging.Psd.Core.Decode.Decompressors;

internal sealed class PsdZipPredictDecompressor : PsdDecompressorBase
{
    protected override CompressionType Compression => CompressionType.ZipPredict;

    protected override PlaneImage Decompress8(PsdBigEndianReader reader, FileHeader header, PlaneRole[] roles, CancellationToken ct)
    {
        var width = header.Width;
        var height = header.Height;

        var planes = AllocatePlanes(header, roles);

        using var z = new ZLibStream(reader.BaseStream, CompressionMode.Decompress, leaveOpen: true);
        var zReader = new PsdBigEndianReader(z);

        var rentedRow = ArrayPool<byte>.Shared.Rent(width);
        try
        {
            var row = rentedRow.AsSpan(0, width);
            for (var p = 0; p < roles.Length; p++)
            {
                var plane = planes[p];
                for (var y = 0; y < height; y++)
                {
                    ct.ThrowIfCancellationRequested();

                    zReader.ReadExactly(row);
                    UndoPredictor8(row);

                    row.CopyTo(plane.Data.AsSpan(y * plane.BytesPerRow, width));
                }
            }

            return new PlaneImage(width, height, 8, planes);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(rentedRow);
        }
    }

    protected override PlaneImage Decompress8Scaled(PsdBigEndianReader reader, FileHeader header, PlaneRole[] roles, int outWidth, int outHeight, CancellationToken ct)
    {
        using var z = new ZLibStream(reader.BaseStream, CompressionMode.Decompress, leaveOpen: true);
        var zReader = new PsdBigEndianReader(z);

        // The shared scaler will allocate a srcWidth row buffer and pass it to the delegate.
        // Then fill that row with decompressed data, undo predictor in-place, and return it.
        return DecodeScaled8PlanesByRows(header.Width, header.Height, outWidth, outHeight, roles, ct, CreatePlaneRowReader);

        ReadNextSourceRow CreatePlaneRowReader() => rowBuffer =>
        {
            zReader.ReadExactly(rowBuffer);
            UndoPredictor8(rowBuffer);
        };
    }

    private static void UndoPredictor8(Span<byte> row)
    {
        for (var x = 1; x < row.Length; x++)
            row[x] = unchecked((byte)(row[x] + row[x - 1]));
    }
}