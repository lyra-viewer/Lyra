using System.Buffers;
using Lyra.Imaging.Psd.Core.Common;
using Lyra.Imaging.Psd.Core.Decode.Pixel;
using Lyra.Imaging.Psd.Core.Readers;
using Lyra.Imaging.Psd.Core.SectionData;

namespace Lyra.Imaging.Psd.Core.Decode.Decompressors;

internal sealed class PsdRawDecompressor : PsdDecompressorBase
{
    protected override CompressionType Compression => CompressionType.Raw;

    protected override PlaneImage Decompress8(PsdBigEndianReader reader, FileHeader header, PlaneRole[] roles, CancellationToken ct)
    {
        var width = header.Width;
        var height = header.Height;

        var planes = AllocatePlanes(header, roles);
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

                    reader.ReadExactly(row);
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
        return DecodeScaled8PlanesByRows(header.Width, header.Height, outWidth, outHeight, roles, ct, () => reader.ReadExactly);
    }
}