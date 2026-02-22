using System.Buffers;
using System.IO.Compression;
using Lyra.Imaging.Psd.Core.Common;
using Lyra.Imaging.Psd.Core.Decode.Pixel;
using Lyra.Imaging.Psd.Core.Readers;
using Lyra.Imaging.Psd.Core.SectionData;

namespace Lyra.Imaging.Psd.Core.Decode.Decompressors;

internal sealed class PsdZipDecompressor : PsdDecompressorBase
{
    protected override PlaneImage Decompress8(PsdBigEndianReader reader, FileHeader header, PlaneRole[] roles, CancellationToken ct)
        => DecompressZip(reader, header, roles, PsdDepth.Bit8, ct);

    protected override PlaneImage Decompress16(PsdBigEndianReader reader, FileHeader header, PlaneRole[] roles, CancellationToken ct)
        => DecompressZip(reader, header, roles, PsdDepth.Bit16, ct);

    protected override PlaneImage Decompress32(PsdBigEndianReader reader, FileHeader header, PlaneRole[] roles, CancellationToken ct)
        => DecompressZip(reader, header, roles, PsdDepth.Bit32, ct);

    protected override PlaneImage Decompress8Preview(PsdBigEndianReader reader, FileHeader header, PlaneRole[] roles, int outWidth, int outHeight, CancellationToken ct)
        => DecompressZipPreview(reader, header, roles, outWidth, outHeight, PsdDepth.Bit8, ct);

    protected override PlaneImage Decompress16Preview(PsdBigEndianReader reader, FileHeader header, PlaneRole[] roles, int outWidth, int outHeight, CancellationToken ct)
        => DecompressZipPreview(reader, header, roles, outWidth, outHeight, PsdDepth.Bit16, ct);

    protected override PlaneImage Decompress32Preview(PsdBigEndianReader reader, FileHeader header, PlaneRole[] roles, int outWidth, int outHeight, CancellationToken ct)
        => DecompressZipPreview(reader, header, roles, outWidth, outHeight, PsdDepth.Bit32, ct);

    private static PlaneImage DecompressZip(PsdBigEndianReader reader, FileHeader header, PlaneRole[] roles, PsdDepth depth, CancellationToken ct)
    {
        var width = header.Width;
        var height = header.Height;

        var rowBytes = depth.RowBytes(width);

        var planes = AllocatePlanes(width, height, (int)depth, roles);

        using var z = new ZLibStream(reader.BaseStream, CompressionMode.Decompress, leaveOpen: true);
        var zReader = new PsdBigEndianReader(z);

        var rentedRow = ArrayPool<byte>.Shared.Rent(rowBytes);
        try
        {
            var row = rentedRow.AsSpan(0, rowBytes);
            for (var p = 0; p < roles.Length; p++)
            {
                var plane = planes[p];
                for (var y = 0; y < height; y++)
                {
                    ct.ThrowIfCancellationRequested();

                    zReader.ReadExactly(row);
                    row.CopyTo(plane.Data.AsSpan(y * plane.BytesPerRow, rowBytes));
                }
            }

            return new PlaneImage(width, height, (int)depth, planes);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(rentedRow);
        }
    }

    private static PlaneImage DecompressZipPreview(PsdBigEndianReader reader, FileHeader header, PlaneRole[] roles, int outWidth, int outHeight, PsdDepth depth, CancellationToken ct)
    {
        using var z = new ZLibStream(reader.BaseStream, CompressionMode.Decompress, leaveOpen: true);
        var zReader = new PsdBigEndianReader(z);

        return DecodeScaledPlanesByRows(header.Width, header.Height, outWidth, outHeight, depth, roles, ct, () => zReader.ReadExactly);
    }
}