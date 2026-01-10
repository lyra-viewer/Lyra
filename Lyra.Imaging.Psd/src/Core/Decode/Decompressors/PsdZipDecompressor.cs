using System.Buffers;
using System.IO.Compression;
using Lyra.Imaging.Psd.Core.Common;
using Lyra.Imaging.Psd.Core.Decode.Pixel;
using Lyra.Imaging.Psd.Core.Readers;
using Lyra.Imaging.Psd.Core.SectionData;

namespace Lyra.Imaging.Psd.Core.Decode.Decompressors;

internal sealed class PsdZipDecompressor : PsdDecompressorBase
{
    protected override CompressionType Compression => CompressionType.Zip;

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

        return DecodeScaled8PlanesByRows(header.Width, header.Height, outWidth, outHeight, roles, ct, () => zReader.ReadExactly);
    }
}