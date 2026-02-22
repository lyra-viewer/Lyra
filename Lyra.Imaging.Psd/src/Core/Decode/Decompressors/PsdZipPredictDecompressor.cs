using System.Buffers;
using System.Buffers.Binary;
using System.IO.Compression;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Lyra.Imaging.Psd.Core.Common;
using Lyra.Imaging.Psd.Core.Decode.Pixel;
using Lyra.Imaging.Psd.Core.Readers;
using Lyra.Imaging.Psd.Core.SectionData;

namespace Lyra.Imaging.Psd.Core.Decode.Decompressors;

internal sealed class PsdZipPredictDecompressor : PsdDecompressorBase
{
    protected override PlaneImage Decompress8(PsdBigEndianReader reader, FileHeader header, PlaneRole[] roles, CancellationToken ct)
        => DecompressZipPredict(reader, header, roles, PsdDepth.Bit8, ct);

    protected override PlaneImage Decompress16(PsdBigEndianReader reader, FileHeader header, PlaneRole[] roles, CancellationToken ct)
        => DecompressZipPredict(reader, header, roles, PsdDepth.Bit16, ct);

    protected override PlaneImage Decompress32(PsdBigEndianReader reader, FileHeader header, PlaneRole[] roles, CancellationToken ct)
        => DecompressZipPredict(reader, header, roles, PsdDepth.Bit32, ct);

    protected override PlaneImage Decompress8Preview(PsdBigEndianReader reader, FileHeader header, PlaneRole[] roles, int outWidth, int outHeight, CancellationToken ct)
        => DecompressZipPredictPreview(reader, header, roles, outWidth, outHeight, PsdDepth.Bit8, ct);

    protected override PlaneImage Decompress16Preview(PsdBigEndianReader reader, FileHeader header, PlaneRole[] roles, int outWidth, int outHeight, CancellationToken ct)
        => DecompressZipPredictPreview(reader, header, roles, outWidth, outHeight, PsdDepth.Bit16, ct);

    protected override PlaneImage Decompress32Preview(PsdBigEndianReader reader, FileHeader header, PlaneRole[] roles, int outWidth, int outHeight, CancellationToken ct)
        => DecompressZipPredictPreview(reader, header, roles, outWidth, outHeight, PsdDepth.Bit32, ct);

    private static PlaneImage DecompressZipPredict(PsdBigEndianReader reader, FileHeader header, PlaneRole[] roles, PsdDepth depth, CancellationToken ct)
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
                    UndoPredictor(row, width, depth);

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

    private static PlaneImage DecompressZipPredictPreview(PsdBigEndianReader reader, FileHeader header, PlaneRole[] roles, int outWidth, int outHeight, PsdDepth depth, CancellationToken ct)
    {
        using var z = new ZLibStream(reader.BaseStream, CompressionMode.Decompress, leaveOpen: true);
        var zReader = new PsdBigEndianReader(z);

        return DecodeScaledPlanesByRows(header.Width, header.Height, outWidth, outHeight, depth, roles, ct, CreatePlaneRowReader);

        ReadNextSourceRow CreatePlaneRowReader() => rowBuffer =>
        {
            zReader.ReadExactly(rowBuffer);
            UndoPredictor(rowBuffer, header.Width, depth);
        };
    }

    private static void UndoPredictor(Span<byte> rowBytes, int widthPixels, PsdDepth depth)
    {
        switch (depth)
        {
            case PsdDepth.Bit8:
                UndoPredictor8(rowBytes);
                return;
            case PsdDepth.Bit16:
                UndoPredictor16(rowBytes, widthPixels);
                return;
            case PsdDepth.Bit32:
                UndoPredictor32(rowBytes, widthPixels);
                return;
            default:
                throw new NotSupportedException($"Predictor undo not supported for depth {depth}.");
        }
    }

    private static void UndoPredictor8(Span<byte> row)
    {
        for (var x = 1; x < row.Length; x++)
            row[x] = unchecked((byte)(row[x] + row[x - 1]));
    }

    private static void UndoPredictor16(Span<byte> rowBytes, int widthPixels)
    {
        if (rowBytes.Length < widthPixels * 2)
            throw new InvalidOperationException($"Predictor16 row length mismatch: got {rowBytes.Length}, expected {widthPixels * 2}.");

        ref var rowRef = ref MemoryMarshal.GetReference(rowBytes);
        var prev = BinaryPrimitives.ReadUInt16BigEndian(MemoryMarshal.CreateReadOnlySpan(ref rowRef, 2));

        for (var x = 1; x < widthPixels; x++)
        {
            var off = x * 2;
            ref var currentRef = ref Unsafe.Add(ref rowRef, off);

            var delta = BinaryPrimitives.ReadUInt16BigEndian(MemoryMarshal.CreateReadOnlySpan(ref currentRef, 2));

            unchecked
            {
                prev = (ushort)(delta + prev);
                BinaryPrimitives.WriteUInt16BigEndian(MemoryMarshal.CreateSpan(ref currentRef, 2), prev);
            }
        }
    }

    private static void UndoPredictor32(Span<byte> rowBytes, int widthPixels)
    {
        if (rowBytes.Length < widthPixels * 4)
            throw new InvalidOperationException($"Predictor32 row length mismatch: got {rowBytes.Length}, expected {widthPixels * 4}.");

        ref var rowRef = ref MemoryMarshal.GetReference(rowBytes);
        var prev = BinaryPrimitives.ReadUInt32BigEndian(MemoryMarshal.CreateReadOnlySpan(ref rowRef, 4));

        for (var x = 1; x < widthPixels; x++)
        {
            var off = x * 4;
            ref var currentRef = ref Unsafe.Add(ref rowRef, off);

            var delta = BinaryPrimitives.ReadUInt32BigEndian(MemoryMarshal.CreateReadOnlySpan(ref currentRef, 4));

            unchecked
            {
                prev = delta + prev;
                BinaryPrimitives.WriteUInt32BigEndian(MemoryMarshal.CreateSpan(ref currentRef, 4), prev);
            }
        }
    }
}