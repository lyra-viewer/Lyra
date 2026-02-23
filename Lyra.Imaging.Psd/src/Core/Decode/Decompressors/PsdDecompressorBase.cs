using System.Buffers;
using System.Runtime.InteropServices;
using Lyra.Imaging.Psd.Core.Common;
using Lyra.Imaging.Psd.Core.Decode.Pixel;
using Lyra.Imaging.Psd.Core.Decode.PlaneRowConsumer;
using Lyra.Imaging.Psd.Core.Readers;
using Lyra.Imaging.Psd.Core.SectionData;

namespace Lyra.Imaging.Psd.Core.Decode.Decompressors;

public abstract class PsdDecompressorBase : IPsdDecompressor
{
    #region Entry Points

    public PlaneImage DecompressPreview(PsdBigEndianReader reader, FileHeader header, int width, int height, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(reader);
        
        ct.ThrowIfCancellationRequested();

        ValidateCommonInputs(header);

        if (width <= 0 || height <= 0)
            throw new InvalidOperationException($"Invalid scaled dimensions: {width}x{height}.");

        if (width > header.Width || height > header.Height)
            throw new InvalidOperationException($"Scaled dimensions {width}x{height} exceed source {header.Width}x{header.Height}.");

        var roles = CompositePlaneRoles.Get(header.ColorMode, header.NumberOfChannels);
        var depth = PsdDepthUtil.FromBitsPerChannel(header.Depth);

        return depth switch
        {
            PsdDepth.Bit8 => Decompress8Preview(reader, header, roles, width, height, ct),
            PsdDepth.Bit16 => Decompress16Preview(reader, header, roles, width, height, ct),
            PsdDepth.Bit32 => Decompress32Preview(reader, header, roles, width, height, ct),
            _ => throw new NotSupportedException($"PSD composite decompress: Depth {header.Depth} not supported. Expected 8, 16, or 32.")
        };
    }

    public void DecompressPlanesRowRegion(PsdBigEndianReader reader, FileHeader header, int yStart, int yEnd, IPlaneRowConsumer consumer, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(consumer);

        ct.ThrowIfCancellationRequested();
        
        ValidateCommonInputs(header);

        if ((uint)yStart > (uint)header.Height || (uint)yEnd > (uint)header.Height || yStart > yEnd)
            throw new ArgumentOutOfRangeException(nameof(yStart), $"Invalid row region [{yStart}, {yEnd}) for height {header.Height}.");

        var roles = CompositePlaneRoles.Get(header.ColorMode, header.NumberOfChannels);
        var depth = PsdDepthUtil.FromBitsPerChannel(header.Depth);

        switch (depth)
        {
            case PsdDepth.Bit8:
                Decompress8RowRegion(reader, header, roles, yStart, yEnd, consumer, ct);
                return;
            case PsdDepth.Bit16:
                Decompress16RowRegion(reader, header, roles, yStart, yEnd, consumer, ct);
                return;
            case PsdDepth.Bit32:
                Decompress32RowRegion(reader, header, roles, yStart, yEnd, consumer, ct);
                return;
            default:
                throw new NotSupportedException($"Row-region decode: Depth {header.Depth} not supported. Expected 8, 16, or 32.");
        }
    }

    #endregion

    #region Helpers

    public static PlaneImage AllocatePlaneImage(FileHeader header)
    {
        var roles = CompositePlaneRoles.Get(header.ColorMode, header.NumberOfChannels);
        var planes = AllocatePlanes(header, roles);
        return new PlaneImage(header.Width, header.Height, header.Depth, planes);
    }

    #endregion

    #region Decompress Routing

    protected abstract PlaneImage Decompress8(PsdBigEndianReader reader, FileHeader header, PlaneRole[] roles, CancellationToken ct);

    protected abstract PlaneImage Decompress16(PsdBigEndianReader reader, FileHeader header, PlaneRole[] roles, CancellationToken ct);

    protected abstract PlaneImage Decompress32(PsdBigEndianReader reader, FileHeader header, PlaneRole[] roles, CancellationToken ct);

    protected abstract PlaneImage Decompress8Preview(PsdBigEndianReader reader, FileHeader header, PlaneRole[] roles, int width, int height, CancellationToken ct);

    protected abstract PlaneImage Decompress16Preview(PsdBigEndianReader reader, FileHeader header, PlaneRole[] roles, int width, int height, CancellationToken ct);

    protected abstract PlaneImage Decompress32Preview(PsdBigEndianReader reader, FileHeader header, PlaneRole[] roles, int width, int height, CancellationToken ct);

    protected virtual void Decompress8RowRegion(PsdBigEndianReader reader, FileHeader header, PlaneRole[] roles, int yStart, int yEnd, IPlaneRowConsumer consumer, CancellationToken ct)
    {
        var full = Decompress8(reader, header, roles, ct);
        EmitRowRegion(full, roles.Length, yStart, yEnd, consumer, ct);
    }

    protected virtual void Decompress16RowRegion(PsdBigEndianReader reader, FileHeader header, PlaneRole[] roles, int yStart, int yEnd, IPlaneRowConsumer consumer, CancellationToken ct)
    {
        var full = Decompress16(reader, header, roles, ct);
        EmitRowRegion(full, roles.Length, yStart, yEnd, consumer, ct);
    }

    protected virtual void Decompress32RowRegion(PsdBigEndianReader reader, FileHeader header, PlaneRole[] roles, int yStart, int yEnd, IPlaneRowConsumer consumer, CancellationToken ct)
    {
        var full = Decompress32(reader, header, roles, ct);
        EmitRowRegion(full, roles.Length, yStart, yEnd, consumer, ct);
    }

    private static void EmitRowRegion(PlaneImage full, int planeCount, int yStart, int yEnd, IPlaneRowConsumer consumer, CancellationToken ct)
    {
        var rowBytes = full.Planes[0].BytesPerRow;

        for (var y = yStart; y < yEnd; y++)
        {
            ct.ThrowIfCancellationRequested();

            for (var p = 0; p < planeCount; p++)
            {
                var plane = full.Planes[p];
                var row = plane.Data.AsSpan(y * plane.BytesPerRow, rowBytes);
                consumer.ConsumeRow(p, y, row);
            }
        }
    }

    #endregion

    #region Scaled Decode Logic

    protected delegate void ReadNextSourceRow(Span<byte> rowBuffer);

    protected static PlaneImage DecodeScaledPlanesByRows(
        int srcWidth,
        int srcHeight,
        int outWidth,
        int outHeight,
        int depthBits,
        PlaneRole[] roles,
        CancellationToken ct,
        Func<ReadNextSourceRow> createPlaneRowReader)
    {
        var depth = PsdDepthUtil.FromBitsPerChannel(depthBits);
        return DecodeScaledPlanesByRows(srcWidth, srcHeight, outWidth, outHeight, depth, roles, ct, createPlaneRowReader);
    }

    protected static PlaneImage DecodeScaledPlanesByRows(
        int srcWidth,
        int srcHeight,
        int outWidth,
        int outHeight,
        PsdDepth depth,
        PlaneRole[] roles,
        CancellationToken ct,
        Func<ReadNextSourceRow> createPlaneRowReader)
    {
        var bpc = depth.BytesPerChannel();

        var planes = AllocatePlanes(outWidth, outHeight, (int)depth, roles);

        var xMap = BuildXMap(srcWidth, outWidth);
        var yMap = BuildYMap(srcHeight, outHeight);

        var srcRowBytes = depth.RowBytes(srcWidth);
        var rentedSrcRow = ArrayPool<byte>.Shared.Rent(srcRowBytes);

        try
        {
            var srcRow = rentedSrcRow.AsSpan(0, srcRowBytes);

            for (var p = 0; p < roles.Length; p++)
            {
                ct.ThrowIfCancellationRequested();

                var readNextRow = createPlaneRowReader();

                var plane = planes[p];
                var nextOutY = 0;
                var targetYSrc = yMap[nextOutY];

                for (var ySrc = 0; ySrc < srcHeight; ySrc++)
                {
                    ct.ThrowIfCancellationRequested();

                    readNextRow(srcRow);

                    while (nextOutY < outHeight && ySrc == targetYSrc)
                    {
                        var dstRow = plane.Data.AsSpan(nextOutY * plane.BytesPerRow, outWidth * bpc);
                        SampleRowNearestBytes(srcRow, dstRow, xMap, bpc);

                        nextOutY++;
                        if (nextOutY >= outHeight) break;
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
            ArrayPool<byte>.Shared.Return(rentedSrcRow);
        }
    }

    protected static int[] BuildXMap(int srcWidth, int dstWidth)
    {
        var map = new int[dstWidth];
        for (var x = 0; x < dstWidth; x++)
            map[x] = (int)((long)x * srcWidth / dstWidth);
        
        return map;
    }

    protected static int[] BuildYMap(int srcHeight, int dstHeight)
    {
        var map = new int[dstHeight];
        for (var y = 0; y < dstHeight; y++)
            map[y] = (int)((long)y * srcHeight / dstHeight);
        
        return map;
    }

    protected static void SampleRowNearestBytes(ReadOnlySpan<byte> srcRow, Span<byte> dstRow, int[] xMap, int bpc)
    {
        if (bpc == 1)
        {
            for (var xOut = 0; xOut < xMap.Length; xOut++)
                dstRow[xOut] = srcRow[xMap[xOut]];
        }
        else if (bpc == 2)
        {
            var src16 = MemoryMarshal.Cast<byte, ushort>(srcRow);
            var dst16 = MemoryMarshal.Cast<byte, ushort>(dstRow);
            for (var xOut = 0; xOut < xMap.Length; xOut++)
                dst16[xOut] = src16[xMap[xOut] >> 1];
        }
        else if (bpc == 4)
        {
            var src32 = MemoryMarshal.Cast<byte, uint>(srcRow);
            var dst32 = MemoryMarshal.Cast<byte, uint>(dstRow);
            for (var xOut = 0; xOut < xMap.Length; xOut++)
                dst32[xOut] = src32[xMap[xOut] >> 2];
        }
        else
        {
            throw new NotSupportedException($"Unsupported bpc: {bpc}.");
        }
    }

    #endregion

    #region Validation & Alloc

    protected static void ValidateCommonInputs(FileHeader header)
    {
        if (header.Width <= 0 || header.Height <= 0)
            throw new InvalidOperationException($"Invalid PSD dimensions: {header.Width}x{header.Height}.");

        if (header.NumberOfChannels <= 0)
            throw new InvalidOperationException($"PSD has {header.NumberOfChannels} channels; expected at least 1.");
    }

    protected static Plane[] AllocatePlanes(FileHeader header, PlaneRole[] roles)
        => AllocatePlanes(header.Width, header.Height, header.Depth, roles);

    protected static Plane[] AllocatePlanes(int width, int height, int depthBits, PlaneRole[] roles)
    {
        var depth = PsdDepthUtil.FromBitsPerChannel(depthBits);
        var bytesPerRow = depth.RowBytes(width);
        var planeSize = checked(bytesPerRow * height);

        var planes = new Plane[roles.Length];
        for (var i = 0; i < roles.Length; i++)
            planes[i] = new Plane(roles[i], new byte[planeSize], bytesPerRow);

        return planes;
    }

    #endregion
}