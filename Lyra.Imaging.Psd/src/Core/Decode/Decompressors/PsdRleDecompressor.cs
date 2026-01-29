using System.Buffers;
using Lyra.Imaging.Psd.Core.Decode.Pixel;
using Lyra.Imaging.Psd.Core.Readers;
using Lyra.Imaging.Psd.Core.SectionData;

namespace Lyra.Imaging.Psd.Core.Decode.Decompressors;

internal sealed class PsdRleDecompressor : PsdDecompressorBase
{
    private const int MaxPackedRowBytes = 16 * 1024 * 1024;

    protected override PlaneImage Decompress8(PsdBigEndianReader reader, FileHeader header, PlaneRole[] roles, CancellationToken ct)
    {
        var width = header.Width;
        var height = header.Height;
        var planeCount = roles.Length;

        // Shared: read + validate row-byte-count table
        var rowByteCounts = ReadAndValidateRowByteCounts(reader, header, planeCount, height, ct);

        // Allocate output planes
        var planes = AllocatePlanes(header, roles);

        var packedRent = ArrayPool<byte>.Shared.Rent(64 * 1024);
        var unpackedRent = ArrayPool<byte>.Shared.Rent(width);
        var state = new RleRowDecodeState(packedRent);

        try
        {
            for (var p = 0; p < planeCount; p++)
            {
                var plane = planes[p];
                for (var y = 0; y < height; y++)
                {
                    ct.ThrowIfCancellationRequested();

                    DecodeNextUnpackedRow(reader, width, rowByteCounts, state, unpackedRent, ct);
                    unpackedRent.AsSpan(0, width)
                        .CopyTo(plane.Data.AsSpan(y * plane.BytesPerRow, width));
                }
            }

            return new PlaneImage(width, height, 8, planes);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(state.PackedBuffer);
            ArrayPool<byte>.Shared.Return(unpackedRent);
        }
    }

    protected override PlaneImage Decompress8Preview(PsdBigEndianReader reader, FileHeader header, PlaneRole[] roles, int outWidth, int outHeight, CancellationToken ct)
    {
        var srcWidth = header.Width;
        var srcHeight = header.Height;
        var planeCount = roles.Length;

        var rowByteCounts = ReadAndValidateRowByteCounts(reader, header, planeCount, srcHeight, ct);

        var planes = AllocatePlanes(outWidth, outHeight, 8, roles);

        var xMap = BuildXMap(srcWidth, outWidth);
        var yMap = BuildYMap(srcHeight, outHeight);

        var packedRent = ArrayPool<byte>.Shared.Rent(64 * 1024);
        var unpackedRent = ArrayPool<byte>.Shared.Rent(srcWidth);
        var state = new RleRowDecodeState(packedRent);

        try
        {
            for (var p = 0; p < planeCount; p++)
            {
                ct.ThrowIfCancellationRequested();

                var plane = planes[p];

                var nextOutY = 0;
                var targetYSrc = yMap[nextOutY];
                for (var ySrc = 0; ySrc < srcHeight; ySrc++)
                {
                    ct.ThrowIfCancellationRequested();

                    // If this source row is NOT needed for output, skip packed bytes without decoding.
                    if (ySrc != targetYSrc)
                    {
                        var packedLen = rowByteCounts[state.RowIndex++];
                        if (packedLen < 0)
                            throw new InvalidOperationException($"Negative PackBits row length: {packedLen}.");

                        reader.Skip(packedLen);
                        continue;
                    }

                    // Needed row: decode, then sample into one or more output rows
                    DecodeNextUnpackedRow(reader, srcWidth, rowByteCounts, state, unpackedRent, ct);

                    while (nextOutY < outHeight && ySrc == targetYSrc)
                    {
                        var dstRow = plane.Data.AsSpan(nextOutY * plane.BytesPerRow, outWidth);
                        SampleRowNearest(unpackedRent.AsSpan(0, srcWidth), dstRow, xMap);

                        nextOutY++;
                        if (nextOutY >= outHeight)
                            break;

                        targetYSrc = yMap[nextOutY];
                    }
                }

                if (nextOutY != outHeight)
                    throw new InvalidOperationException($"Scaled decode bug: produced {nextOutY} rows out of {outHeight}.");
            }

            return new PlaneImage(outWidth, outHeight, 8, planes);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(state.PackedBuffer);
            ArrayPool<byte>.Shared.Return(unpackedRent);
        }
    }

    protected override void Decompress8RowRegion(PsdBigEndianReader reader, FileHeader header, PlaneRole[] roles, int yStart, int yEnd, IPlaneRowConsumer consumer, CancellationToken ct)
    {
        var width = header.Width;
        var height = header.Height;
        var planeCount = roles.Length;

        // Shared: read + validate row-byte-count table
        var rowByteCounts = ReadAndValidateRowByteCounts(reader, header, planeCount, height, ct);

        var packedRent = ArrayPool<byte>.Shared.Rent(64 * 1024);
        var unpackedRent = ArrayPool<byte>.Shared.Rent(width);
        var state = new RleRowDecodeState(packedRent);

        try
        {
            for (var p = 0; p < planeCount; p++)
            {
                ct.ThrowIfCancellationRequested();

                for (var y = 0; y < height; y++)
                {
                    ct.ThrowIfCancellationRequested();

                    // Outside requested region: skip packed bytes without decoding.
                    if (y < yStart || y >= yEnd)
                    {
                        if ((uint)state.RowIndex >= (uint)rowByteCounts.Length)
                            throw new InvalidOperationException("Row index exceeded row-byte-count table length.");

                        var packedLen = rowByteCounts[state.RowIndex++];
                        if (packedLen < 0)
                            throw new InvalidOperationException($"Negative PackBits row length: {packedLen}.");

                        reader.Skip(packedLen);
                        continue;
                    }

                    // Needed row: decode and forward to consumer
                    DecodeNextUnpackedRow(reader, width, rowByteCounts, state, unpackedRent, ct);
                    consumer.ConsumeRow(p, y, unpackedRent.AsSpan(0, width));
                }
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(state.PackedBuffer);
            ArrayPool<byte>.Shared.Return(unpackedRent);
        }
    }


    private sealed class RleRowDecodeState(byte[] initialPackedBuffer)
    {
        public int RowIndex;
        public byte[] PackedBuffer = initialPackedBuffer;
    }

    private int[] ReadAndValidateRowByteCounts(PsdBigEndianReader reader, FileHeader header, int planeCount, int height, CancellationToken ct)
    {
        var version = header.Version;
        var rowCount = planeCount * height;
        var rowByteCounts = new int[rowCount];
        for (var i = 0; i < rowCount; i++)
        {
            ct.ThrowIfCancellationRequested();

            var len = version == 2 ? reader.ReadInt32() : reader.ReadUInt16();
            if ((uint)len > MaxPackedRowBytes)
                throw new InvalidOperationException($"Suspicious PackBits row length: {len} bytes.");

            rowByteCounts[i] = len;
        }

        return rowByteCounts;
    }

    private static void DecodeNextUnpackedRow(PsdBigEndianReader reader, int rowWidth, int[] rowByteCounts, RleRowDecodeState state, byte[] unpackedRent, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        if ((uint)state.RowIndex >= (uint)rowByteCounts.Length)
            throw new InvalidOperationException("Row index exceeded row-byte-count table length.");

        var packedLen = rowByteCounts[state.RowIndex++];
        if (packedLen < 0)
            throw new InvalidOperationException($"Negative PackBits row length: {packedLen}.");

        if (packedLen > state.PackedBuffer.Length)
        {
            ArrayPool<byte>.Shared.Return(state.PackedBuffer);
            state.PackedBuffer = ArrayPool<byte>.Shared.Rent(packedLen);
        }

        reader.ReadExactly(state.PackedBuffer.AsSpan(0, packedLen));

        PackBitsDecode(state.PackedBuffer.AsSpan(0, packedLen), unpackedRent.AsSpan(0, rowWidth));
    }

    private static void PackBitsDecode(ReadOnlySpan<byte> src, Span<byte> dst)
    {
        var si = 0;
        var di = 0;
        while (di < dst.Length)
        {
            if (si >= src.Length)
                throw new InvalidOperationException("PackBits source exhausted.");

            var n = unchecked((sbyte)src[si++]);
            if (n >= 0)
            {
                var count = n + 1;
                src.Slice(si, count).CopyTo(dst.Slice(di, count));
                si += count;
                di += count;
            }
            else if (n != -128)
            {
                var count = 1 - n;
                var val = src[si++];
                dst.Slice(di, count).Fill(val);
                di += count;
            }
        }
    }

    private static void ValidateRlePayload(ImageData imageData, int planeCount, int height, int[] rowByteCounts, int version)
    {
        // MEMO: Maybe useful in the future, now it sucks to wire it

        long sumPacked = 0;
        foreach (var len in rowByteCounts)
        {
            if (len is < 0 or > MaxPackedRowBytes)
                throw new InvalidOperationException($"Invalid RLE row length: {len}.");

            sumPacked += len;
        }

        var bytesPerEntry = version == 2 ? 4 : 2;
        var tableBytes = (long)planeCount * height * bytesPerEntry;
        var expectedPayload = tableBytes + sumPacked;
        if (expectedPayload != imageData.PayloadLength)
        {
            throw new InvalidOperationException($"Invalid PSD RLE payload.\n" +
                                                $"Expected: {expectedPayload}, Actual: {imageData.PayloadLength}");
        }
    }
}