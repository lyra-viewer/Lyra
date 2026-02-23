using System.Buffers;
using Lyra.Imaging.Psd.Core.Common;
using Lyra.Imaging.Psd.Core.Decode.Pixel;
using Lyra.Imaging.Psd.Core.Decode.PlaneRowConsumer;
using Lyra.Imaging.Psd.Core.Readers;
using Lyra.Imaging.Psd.Core.SectionData;

namespace Lyra.Imaging.Psd.Core.Decode.Decompressors;

internal sealed class PsdRawDecompressor : PsdDecompressorBase
{
    protected override PlaneImage Decompress8(PsdBigEndianReader reader, FileHeader header, PlaneRole[] roles, CancellationToken ct)
        => DecompressRaw(reader, header, roles, PsdDepth.Bit8, ct);

    protected override PlaneImage Decompress16(PsdBigEndianReader reader, FileHeader header, PlaneRole[] roles, CancellationToken ct)
        => DecompressRaw(reader, header, roles, PsdDepth.Bit16, ct);

    protected override PlaneImage Decompress32(PsdBigEndianReader reader, FileHeader header, PlaneRole[] roles, CancellationToken ct)
        => DecompressRaw(reader, header, roles, PsdDepth.Bit32, ct);

    protected override PlaneImage Decompress8Preview(PsdBigEndianReader reader, FileHeader header, PlaneRole[] roles, int outWidth, int outHeight, CancellationToken ct)
        => DecodeScaledPlanesByRows(header.Width, header.Height, outWidth, outHeight, PsdDepth.Bit8, roles, ct, () => reader.ReadExactly);

    protected override PlaneImage Decompress16Preview(PsdBigEndianReader reader, FileHeader header, PlaneRole[] roles, int outWidth, int outHeight, CancellationToken ct)
        => DecodeScaledPlanesByRows(header.Width, header.Height, outWidth, outHeight, PsdDepth.Bit16, roles, ct, () => reader.ReadExactly);

    protected override PlaneImage Decompress32Preview(PsdBigEndianReader reader, FileHeader header, PlaneRole[] roles, int outWidth, int outHeight, CancellationToken ct)
        => DecodeScaledPlanesByRows(header.Width, header.Height, outWidth, outHeight, PsdDepth.Bit32, roles, ct, () => reader.ReadExactly);

    protected override void Decompress8RowRegion(PsdBigEndianReader reader, FileHeader header, PlaneRole[] roles, int yStart, int yEnd, IPlaneRowConsumer consumer, CancellationToken ct)
        => DecompressRawRowRegion(reader, header, roles, PsdDepth.Bit8, yStart, yEnd, consumer, ct);

    protected override void Decompress16RowRegion(PsdBigEndianReader reader, FileHeader header, PlaneRole[] roles, int yStart, int yEnd, IPlaneRowConsumer consumer, CancellationToken ct)
        => DecompressRawRowRegion(reader, header, roles, PsdDepth.Bit16, yStart, yEnd, consumer, ct);

    protected override void Decompress32RowRegion(PsdBigEndianReader reader, FileHeader header, PlaneRole[] roles, int yStart, int yEnd, IPlaneRowConsumer consumer, CancellationToken ct)
        => DecompressRawRowRegion(reader, header, roles, PsdDepth.Bit32, yStart, yEnd, consumer, ct);

    private static PlaneImage DecompressRaw(PsdBigEndianReader reader, FileHeader header, PlaneRole[] roles, PsdDepth depth, CancellationToken ct)
    {
        var width = header.Width;
        var height = header.Height;

        var rowBytes = depth.RowBytes(width);

        var planes = AllocatePlanes(width, height, (int)depth, roles);
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

                    reader.ReadExactly(row);
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

    private static void DecompressRawRowRegion(
        PsdBigEndianReader reader,
        FileHeader header,
        PlaneRole[] roles,
        PsdDepth depth,
        int yStart,
        int yEnd,
        IPlaneRowConsumer consumer,
        CancellationToken ct)
    {
        var width = header.Width;
        var height = header.Height;

        var rowBytes = depth.RowBytes(width);

        var basePos = reader.BaseStream.Position;

        var rentedRow = ArrayPool<byte>.Shared.Rent(rowBytes);
        try
        {
            var row = rentedRow.AsSpan(0, rowBytes);

            for (var p = 0; p < roles.Length; p++)
            {
                ct.ThrowIfCancellationRequested();

                var planeStart = basePos + (long)p * height * rowBytes;
                var regionStart = planeStart + (long)yStart * rowBytes;

                reader.BaseStream.Position = regionStart;

                for (var y = yStart; y < yEnd; y++)
                {
                    ct.ThrowIfCancellationRequested();

                    reader.ReadExactly(row);
                    consumer.ConsumeRow(p, y, row);
                }
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(rentedRow);
        }
    }
}