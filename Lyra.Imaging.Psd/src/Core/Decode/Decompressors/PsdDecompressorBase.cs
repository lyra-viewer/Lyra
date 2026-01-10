using System.Buffers;
using Lyra.Imaging.Psd.Core.Common;
using Lyra.Imaging.Psd.Core.Decode.Pixel;
using Lyra.Imaging.Psd.Core.Readers;
using Lyra.Imaging.Psd.Core.SectionData;

namespace Lyra.Imaging.Psd.Core.Decode.Decompressors;

public abstract class PsdDecompressorBase : IPsdDecompressor
{
    protected abstract CompressionType Compression { get; } // TODO remove later

    #region Entry Points

    public virtual void ValidatePayload(FileHeader header, ImageData imageData)
    {
        // Default: nothing to validate for most compression types.
        // RLE overrides this to validate its row-byte-count table / total payload.
    }

    public PlaneImage DecompressPlanes(PsdBigEndianReader reader, FileHeader header, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(reader);

        ct.ThrowIfCancellationRequested();

        ValidateCommonInputs(header);

        var roles = CompositePlaneRoles.Get(header.ColorMode, header.NumberOfChannels);
        return header.Depth switch
        {
            8 => Decompress8(reader, header, roles, ct),
            16 => Decompress16(reader, header, roles, ct),
            32 => Decompress32(reader, header, roles, ct),
            _ => throw new NotSupportedException($"PSD composite decompress: Depth {header.Depth} not supported. Expected 8, 16, or 32.")
        };
    }

    public PlaneImage DecompressPlanesScaled(PsdBigEndianReader reader, FileHeader header, int width, int height, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ct.ThrowIfCancellationRequested();

        ValidateCommonInputs(header);

        if (width <= 0 || height <= 0)
            throw new InvalidOperationException($"Invalid scaled dimensions: {width}x{height}.");

        if (width > header.Width || height > header.Height)
            throw new InvalidOperationException($"Scaled dimensions {width}x{height} exceed source {header.Width}x{header.Height}.");

        var roles = CompositePlaneRoles.Get(header.ColorMode, header.NumberOfChannels);
        return header.Depth switch
        {
            8 => Decompress8Scaled(reader, header, roles, width, height, ct),
            16 => Decompress16Scaled(reader, header, roles, width, height, ct),
            32 => Decompress32Scaled(reader, header, roles, width, height, ct),
            _ => throw new NotSupportedException($"PSD composite decompress: Depth {header.Depth} not supported. Expected 8, 16, or 32.")
        };
    }

    #endregion

    #region Decompress Routing

    protected abstract PlaneImage Decompress8(PsdBigEndianReader reader, FileHeader header, PlaneRole[] roles, CancellationToken ct);

    protected virtual PlaneImage Decompress16(PsdBigEndianReader reader, FileHeader header, PlaneRole[] roles, CancellationToken ct)
        => throw new NotSupportedException($"PSD composite decompress ({Compression}): 16-bit depth not implemented.");

    protected virtual PlaneImage Decompress32(PsdBigEndianReader reader, FileHeader header, PlaneRole[] roles, CancellationToken ct)
        => throw new NotSupportedException($"PSD composite decompress ({Compression}): 32-bit depth not implemented.");

    protected virtual PlaneImage Decompress8Scaled(PsdBigEndianReader reader, FileHeader header, PlaneRole[] roles, int width, int height, CancellationToken ct)
        => throw new NotSupportedException($"PSD composite decompress ({Compression}): scaled 8-bit decode not implemented.");

    protected virtual PlaneImage Decompress16Scaled(PsdBigEndianReader reader, FileHeader header, PlaneRole[] roles, int width, int height, CancellationToken ct)
        => throw new NotSupportedException($"PSD composite decompress ({Compression}): scaled 16-bit decode not implemented.");

    protected virtual PlaneImage Decompress32Scaled(PsdBigEndianReader reader, FileHeader header, PlaneRole[] roles, int width, int height, CancellationToken ct)
        => throw new NotSupportedException($"PSD composite decompress ({Compression}): scaled 32-bit decode not implemented.");

    #endregion

    #region Scaled Decode Logic

    /// <summary>
    /// Reads the next decoded source row for the current plane into <paramref name="rowBuffer"/>.
    /// Implementations must fully fill the span, and advance the underlying input stream accordingly.
    /// </summary>
    protected delegate void ReadNextSourceRow(Span<byte> rowBuffer);

    /// <summary>
    /// Shared helper for 8-bit planar sources:
    /// <list type="bullet">
    ///   <item><description>Allocates scaled output planes (<c>outWidth × outHeight</c>).</description></item>
    ///   <item><description>Uses row-by-row decoded input via <paramref name="readNextRow"/>.</description></item>
    ///   <item><description>Performs nearest-neighbor downsampling into destination planes.</description></item>
    /// </list>
    /// </summary>
    protected static PlaneImage DecodeScaled8PlanesByRows(
        int srcWidth,
        int srcHeight,
        int outWidth,
        int outHeight,
        PlaneRole[] roles,
        CancellationToken ct,
        Func<ReadNextSourceRow> createPlaneRowReader)
    {
        var planes = AllocatePlanes(outWidth, outHeight, 8, roles);

        var xMap = BuildXMap(srcWidth, outWidth);
        var yMap = BuildYMap(srcHeight, outHeight);

        var rentedSrcRow = ArrayPool<byte>.Shared.Rent(srcWidth);
        try
        {
            for (var p = 0; p < roles.Length; p++)
            {
                ct.ThrowIfCancellationRequested();

                var readNextRow = createPlaneRowReader();
                var srcRow = rentedSrcRow.AsSpan(0, srcWidth);

                var plane = planes[p];
                var nextOutY = 0; // next output row to write
                var targetYSrc = yMap[nextOutY];

                for (var ySrc = 0; ySrc < srcHeight; ySrc++)
                {
                    ct.ThrowIfCancellationRequested();

                    // Always consume the next source row to keep alignment correct
                    readNextRow(srcRow);

                    // If this source row is used for one or more output rows, write them
                    while (nextOutY < outHeight && ySrc == targetYSrc)
                    {
                        var dstRow = plane.Data.AsSpan(nextOutY * plane.BytesPerRow, outWidth);
                        SampleRowNearest(srcRow, dstRow, xMap);

                        nextOutY++;
                        if (nextOutY >= outHeight) break;
                        targetYSrc = yMap[nextOutY];
                    }
                }

                // Sanity: should have produced all output rows
                if (nextOutY != outHeight)
                    throw new InvalidOperationException($"Scaled decode bug: produced {nextOutY} rows out of {outHeight}.");
            }

            return new PlaneImage(outWidth, outHeight, 8, planes);
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

    protected static void SampleRowNearest(ReadOnlySpan<byte> srcRow, Span<byte> dstRow, int[] xMap)
    {
        for (var xOut = 0; xOut < dstRow.Length; xOut++)
            dstRow[xOut] = srcRow[xMap[xOut]];
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
        var bpc = depthBits switch
        {
            8 => 1,
            16 => 2,
            32 => 4,
            _ => throw new NotSupportedException($"Depth {depthBits} not supported for plane allocation.")
        };

        var bytesPerRow = checked(width * bpc);
        var planeSize = checked(bytesPerRow * height);

        var planes = new Plane[roles.Length];
        for (var i = 0; i < roles.Length; i++)
            planes[i] = new Plane(roles[i], new byte[planeSize], bytesPerRow);

        return planes;
    }

    #endregion
}