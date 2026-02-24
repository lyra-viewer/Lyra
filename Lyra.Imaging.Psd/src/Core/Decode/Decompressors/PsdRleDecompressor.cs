using System.Buffers;
using Lyra.Imaging.Psd.Core.Common;
using Lyra.Imaging.Psd.Core.Decode.Pixel;
using Lyra.Imaging.Psd.Core.Decode.PlaneRowConsumer;
using Lyra.Imaging.Psd.Core.Readers;
using Lyra.Imaging.Psd.Core.SectionData;

namespace Lyra.Imaging.Psd.Core.Decode.Decompressors;

internal sealed class PsdRleDecompressor : PsdDecompressorBase
{
    private const int MaxPackedRowBytes = 16 * 1024 * 1024;

    private int[]? _cachedRowByteCounts;
    private long[]? _cachedPrefixSum;
    private long _cachedPackedDataStart;

    protected override PlaneImage Decompress8(PsdBigEndianReader reader, FileHeader header, PlaneRole[] roles, CancellationToken ct)
        => DecompressRle(reader, header, roles, PsdDepth.Bit8, ct);

    protected override PlaneImage Decompress16(PsdBigEndianReader reader, FileHeader header, PlaneRole[] roles, CancellationToken ct)
        => DecompressRle(reader, header, roles, PsdDepth.Bit16, ct);

    protected override PlaneImage Decompress32(PsdBigEndianReader reader, FileHeader header, PlaneRole[] roles, CancellationToken ct)
        => DecompressRle(reader, header, roles, PsdDepth.Bit32, ct);

    protected override PlaneImage Decompress8Preview(PsdBigEndianReader reader, FileHeader header, PlaneRole[] roles, int outWidth, int outHeight, CancellationToken ct)
        => DecompressRlePreview(reader, header, roles, outWidth, outHeight, PsdDepth.Bit8, ct);

    protected override PlaneImage Decompress16Preview(PsdBigEndianReader reader, FileHeader header, PlaneRole[] roles, int outWidth, int outHeight, CancellationToken ct)
        => DecompressRlePreview(reader, header, roles, outWidth, outHeight, PsdDepth.Bit16, ct);

    protected override PlaneImage Decompress32Preview(PsdBigEndianReader reader, FileHeader header, PlaneRole[] roles, int outWidth, int outHeight, CancellationToken ct)
        => DecompressRlePreview(reader, header, roles, outWidth, outHeight, PsdDepth.Bit32, ct);

    protected override void Decompress8RowRegion(PsdBigEndianReader reader, FileHeader header, PlaneRole[] roles, int yStart, int yEnd, IPlaneRowConsumer consumer, CancellationToken ct)
        => DecompressRleRowRegion(reader, header, roles, PsdDepth.Bit8, yStart, yEnd, consumer, ct);

    protected override void Decompress16RowRegion(PsdBigEndianReader reader, FileHeader header, PlaneRole[] roles, int yStart, int yEnd, IPlaneRowConsumer consumer, CancellationToken ct)
        => DecompressRleRowRegion(reader, header, roles, PsdDepth.Bit16, yStart, yEnd, consumer, ct);

    protected override void Decompress32RowRegion(PsdBigEndianReader reader, FileHeader header, PlaneRole[] roles, int yStart, int yEnd, IPlaneRowConsumer consumer, CancellationToken ct)
        => DecompressRleRowRegion(reader, header, roles, PsdDepth.Bit32, yStart, yEnd, consumer, ct);

    private PlaneImage DecompressRle(PsdBigEndianReader reader, FileHeader header, PlaneRole[] roles, PsdDepth depth, CancellationToken ct)
    {
        var width = header.Width;
        var height = header.Height;

        var fileChannelCount = header.NumberOfChannels;
        var decodedPlaneCount = roles.Length;

        var rowWidthBytes = depth.RowBytes(width);
        var rowByteCounts = ReadAndValidateRowByteCounts(reader, header, fileChannelCount, height, ct);
        var planes = AllocatePlanes(width, height, (int)depth, roles);

        var state = new RleRowDecodeState(ArrayPool<byte>.Shared.Rent(64 * 1024));

        try
        {
            for (var p = 0; p < decodedPlaneCount; p++)
            {
                var plane = planes[p];

                state.RowIndex = p * height;

                for (var y = 0; y < height; y++)
                {
                    ct.ThrowIfCancellationRequested();

                    var dstRow = plane.Data.AsSpan(y * plane.BytesPerRow, rowWidthBytes);
                    DecodeNextUnpackedRow(reader, rowByteCounts, state, dstRow, ct);
                }
            }

            return new PlaneImage(width, height, (int)depth, planes);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(state.PackedBuffer);
        }
    }

    private PlaneImage DecompressRlePreview(PsdBigEndianReader reader, FileHeader header, PlaneRole[] roles, int outWidth, int outHeight, PsdDepth depth, CancellationToken ct)
    {
        var srcWidth = header.Width;
        var srcHeight = header.Height;

        var fileChannelCount = header.NumberOfChannels;
        var decodedPlaneCount = roles.Length;

        var bpc = depth.BytesPerChannel();
        var srcRowBytes = depth.RowBytes(srcWidth);

        var rowByteCounts = ReadAndValidateRowByteCounts(reader, header, fileChannelCount, srcHeight, ct);

        var planes = AllocatePlanes(outWidth, outHeight, (int)depth, roles);

        var xMap = BuildXMap(srcWidth, outWidth);
        var yMap = BuildYMap(srcHeight, outHeight);

        var state = new RleRowDecodeState(ArrayPool<byte>.Shared.Rent(64 * 1024));
        var unpackedRent = ArrayPool<byte>.Shared.Rent(srcRowBytes);

        try
        {
            for (var p = 0; p < decodedPlaneCount; p++)
            {
                ct.ThrowIfCancellationRequested();

                var plane = planes[p];

                state.RowIndex = p * srcHeight;

                var nextOutY = 0;
                var targetYSrc = yMap[nextOutY];

                for (var ySrc = 0; ySrc < srcHeight; ySrc++)
                {
                    ct.ThrowIfCancellationRequested();

                    if (ySrc != targetYSrc)
                    {
                        var packedLen = rowByteCounts[state.RowIndex++];
                        if (packedLen < 0)
                            throw new InvalidOperationException($"Negative PackBits row length: {packedLen}.");

                        reader.Skip(packedLen);
                        continue;
                    }

                    // decode into temp row (preview needs it for sampling)
                    var tmpRow = unpackedRent.AsSpan(0, srcRowBytes);
                    DecodeNextUnpackedRow(reader, rowByteCounts, state, tmpRow, ct);

                    while (nextOutY < outHeight && ySrc == targetYSrc)
                    {
                        var dstRow = plane.Data.AsSpan(nextOutY * plane.BytesPerRow, outWidth * bpc);
                        SampleRowNearestBytes(tmpRow, dstRow, xMap, bpc);

                        nextOutY++;
                        if (nextOutY >= outHeight)
                            break;

                        targetYSrc = yMap[nextOutY];
                    }
                }

                if (nextOutY != outHeight)
                    throw new InvalidOperationException($"Scaled decode bug: produced {nextOutY} rows out of {outHeight}.");
            }

            return new PlaneImage(outWidth, outHeight, (int)depth, planes);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(state.PackedBuffer);
            ArrayPool<byte>.Shared.Return(unpackedRent);
        }
    }

    private void DecompressRleRowRegion(
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

        var fileChannelCount = header.NumberOfChannels;
        var decodedPlaneCount = roles.Length;
        var rowBytes = depth.RowBytes(width);

        if (_cachedRowByteCounts == null)
        {
            _cachedRowByteCounts = ReadAndValidateRowByteCounts(reader, header, fileChannelCount, height, ct);
            _cachedPackedDataStart = reader.BaseStream.Position;

            _cachedPrefixSum = new long[_cachedRowByteCounts.Length + 1];
            for (var i = 0; i < _cachedRowByteCounts.Length; i++)
                _cachedPrefixSum[i + 1] = _cachedPrefixSum[i] + _cachedRowByteCounts[i];
        }

        var rowByteCounts = _cachedRowByteCounts;
        var prefix = _cachedPrefixSum!;
        var packedDataStart = _cachedPackedDataStart;

        var unpackedRent = ArrayPool<byte>.Shared.Rent(rowBytes);

        var state = new RleRowDecodeState(ArrayPool<byte>.Shared.Rent(64 * 1024));

        try
        {
            for (var p = 0; p < decodedPlaneCount; p++)
            {
                ct.ThrowIfCancellationRequested();

                var rowIndexBase = p * height;
                state.RowIndex = rowIndexBase + yStart;

                var packedOffset = prefix[rowIndexBase + yStart];
                reader.BaseStream.Position = packedDataStart + packedOffset;

                for (var y = yStart; y < yEnd; y++)
                {
                    ct.ThrowIfCancellationRequested();

                    DecodeNextUnpackedRow(reader, rowByteCounts, state, unpackedRent.AsSpan(0, rowBytes), ct);
                    consumer.ConsumeRow(p, y, unpackedRent.AsSpan(0, rowBytes));
                }
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(state.PackedBuffer);
            ArrayPool<byte>.Shared.Return(unpackedRent);
        }
    }

    private int[] ReadAndValidateRowByteCounts(PsdBigEndianReader reader, FileHeader header, int channelCount, int height, CancellationToken ct)
    {
        var version = header.Version;
        var rowCount = checked(channelCount * height);
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

    private static void DecodeNextUnpackedRow(PsdBigEndianReader reader, int[] rowByteCounts, RleRowDecodeState state, Span<byte> dstRow, CancellationToken ct)
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
        PackBitsDecodeChecked(state.PackedBuffer.AsSpan(0, packedLen), dstRow);
    }

    private static void PackBitsDecodeChecked(ReadOnlySpan<byte> src, Span<byte> dst)
    {
        int si = 0, di = 0;
        while ((uint)di < (uint)dst.Length)
        {
            if ((uint)si >= (uint)src.Length)
                throw new InvalidOperationException("PackBits source exhausted.");

            sbyte n = unchecked((sbyte)src[si++]);
            if (n >= 0)
            {
                int count = n + 1;
                if (si + count > src.Length)
                    throw new InvalidOperationException("PackBits literal overruns source.");

                if (di + count > dst.Length)
                    throw new InvalidOperationException("PackBits literal overruns destination.");

                src.Slice(si, count).CopyTo(dst.Slice(di, count));
                si += count;
                di += count;
            }
            else if (n != -128)
            {
                int count = 1 - n;
                if ((uint)si >= (uint)src.Length)
                    throw new InvalidOperationException("PackBits replicate missing byte.");

                if (di + count > dst.Length)
                    throw new InvalidOperationException("PackBits replicate overruns destination.");

                byte val = src[si++];
                dst.Slice(di, count).Fill(val);
                di += count;
            }
        }
    }

    private sealed class RleRowDecodeState(byte[] initialPackedBuffer)
    {
        public int RowIndex;
        public byte[] PackedBuffer = initialPackedBuffer;
    }
}