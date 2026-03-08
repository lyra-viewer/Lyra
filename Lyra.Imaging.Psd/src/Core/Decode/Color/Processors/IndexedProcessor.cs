using System.Buffers;
using Lyra.Imaging.Psd.Core.Common;
using Lyra.Imaging.Psd.Core.Decode.ColorCalibration;
using Lyra.Imaging.Psd.Core.Decode.ColorCalibration.Rgb;
using Lyra.Imaging.Psd.Core.Decode.Pixel;
using Lyra.Imaging.Psd.Core.SectionData;
using Wacton.Unicolour;

namespace Lyra.Imaging.Psd.Core.Decode.Color.Processors;

public sealed class IndexedProcessor : IColorModeProcessor
{
    public string? IccProfileUsed { get; private set; }

    private static readonly RgbCalibrationProvider CalibrationProvider = new();

    public RgbaSurface Process(PlaneImage src, ColorModeContext ctx, ColorModeData? colorModeData, CancellationToken ct)
    {
        var depth = PsdDepthUtil.FromBitsPerChannel(src.BitsPerChannel);
        var bpcBytes = depth.BytesPerChannel();
        var rowBytes = depth.RowBytes(src.Width);

        var index = src.GetPlaneOrThrow(PlaneRole.Index);
        src.TryGetPlane(PlaneRole.A, out var alpha);
        
        if (index.BytesPerRow < rowBytes)
            throw new InvalidOperationException("Index plane BytesPerRow is smaller than Width*bpc for Indexed.");

        if (alpha.Data != null && alpha.BytesPerRow < rowBytes)
            throw new InvalidOperationException("Alpha plane BytesPerRow is smaller than Width*bpc for Indexed.");

        var palette = ctx.IndexedPaletteRgb ?? colorModeData?.Data;
        if (palette == null || palette.Length < 768)
            throw new InvalidOperationException("Indexed color mode requires a 768-byte RGB palette.");

        var rPal = palette.AsSpan(0, 256);
        var gPal = palette.AsSpan(256, 256);
        var bPal = palette.AsSpan(512, 256);

        var stride = checked(src.Width * 4);
        var size = checked(stride * src.Height);

        // ICC calibration (treat Indexed as RGB after palette expansion)
        const int gridSize = RgbCalibrationDefaults.GridSize;
        var calibration = CalibrationProvider.GetCalibration(
            new RgbCalibrationRequest(
                SourceColorMode: ColorMode.Rgb,
                EmbeddedIccProfile: ctx.IccProfile,
                PreferColorManagement: ctx.PreferColorManagement,
                GridSize: gridSize),
            config => BuildCalibrationLuts(src, index, palette, config, gridSize, ct),
            out var iccProfileUsed
        );

        IccProfileUsed = iccProfileUsed;

        var useCalibration = ctx.PreferColorManagement && !calibration.IsIdentity;

        var owner = MemoryPool<byte>.Shared.Rent(size);
        var surface = new RgbaSurface(src.Width, src.Height, owner, stride, ctx.OutputFormat);

        // Palette-expanded 8-bit RGB rows
        var rRowBuf = ArrayPool<byte>.Shared.Rent(src.Width);
        var gRowBuf = ArrayPool<byte>.Shared.Rent(src.Width);
        var bRowBuf = ArrayPool<byte>.Shared.Rent(src.Width);

        // For 16/32: convert index (and alpha) rows to 8-bit first
        byte[]? idx8Rent = null;
        byte[]? a8Rent = null;

        if (bpcBytes != 1)
        {
            idx8Rent = ArrayPool<byte>.Shared.Rent(src.Width);
            if (alpha.Data != null)
                a8Rent = ArrayPool<byte>.Shared.Rent(src.Width);
        }

        try
        {
            for (var y = 0; y < src.Height; y++)
            {
                ct.ThrowIfCancellationRequested();

                var idxRowRaw = index.Data.AsSpan(y * index.BytesPerRow, rowBytes);

                ReadOnlySpan<byte> idxRow8;
                ReadOnlySpan<byte> aRow8 = default;
                var hasAlpha = alpha.Data != null;

                if (bpcBytes == 1)
                {
                    idxRow8 = idxRowRaw[..src.Width];
                    if (hasAlpha)
                        aRow8 = alpha.Data.AsSpan(y * alpha.BytesPerRow, src.Width);
                }
                else
                {
                    var idx8 = idx8Rent!.AsSpan(0, src.Width);

                    if (bpcBytes == 2)
                        PsdSampleConvert.Row16BeTo8(idxRowRaw, idx8);
                    else
                        PsdSampleConvert.Row32FloatBeTo8(idxRowRaw, idx8);

                    idxRow8 = idx8;

                    if (hasAlpha)
                    {
                        var aRaw = alpha.Data.AsSpan(y * alpha.BytesPerRow, rowBytes);
                        var aa = a8Rent!.AsSpan(0, src.Width);

                        if (bpcBytes == 2)
                            PsdSampleConvert.Row16BeTo8(aRaw, aa);
                        else
                            PsdSampleConvert.Row32FloatBeTo8(aRaw, aa);

                        aRow8 = aa;
                    }
                }

                var rRow = rRowBuf.AsSpan(0, src.Width);
                var gRow = gRowBuf.AsSpan(0, src.Width);
                var bRow = bRowBuf.AsSpan(0, src.Width);

                // Palette expansion
                for (var x = 0; x < src.Width; x++)
                {
                    var p = idxRow8[x]; // already 0..255
                    rRow[x] = rPal[p];
                    gRow[x] = gPal[p];
                    bRow[x] = bPal[p];
                }

                var dstRow = surface.GetRowSpan(y);

                PixelRowWriter.WriteRgbRow(
                    dstRow,
                    ctx.OutputFormat.PixelFormat,
                    ctx.OutputFormat.AlphaType,
                    rRow,
                    gRow,
                    bRow,
                    hasAlpha ? aRow8 : default,
                    hasAlpha,
                    useCalibration,
                    calibration);
            }

            return surface;
        }
        catch
        {
            surface.Dispose();
            throw;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(rRowBuf);
            ArrayPool<byte>.Shared.Return(gRowBuf);
            ArrayPool<byte>.Shared.Return(bRowBuf);

            if (idx8Rent != null)
                ArrayPool<byte>.Shared.Return(idx8Rent);
            
            if (a8Rent != null)
                ArrayPool<byte>.Shared.Return(a8Rent);
        }
    }

    private static RgbLuts BuildCalibrationLuts(
        PlaneImage src,
        Plane index,
        byte[] palette,
        Configuration config,
        int gridSize,
        CancellationToken ct)
    {
        var bpcBytes = src.BitsPerChannel switch
        {
            8 => 1,
            16 => 2,
            32 => 4,
            _ => throw new NotSupportedException($"Calibration: BitsPerChannel {src.BitsPerChannel} not supported (expected 8/16/32).")
        };

        var rowBytes = checked(src.Width * bpcBytes);

        var rPal = palette.AsSpan(0, 256);
        var gPal = palette.AsSpan(256, 256);
        var bPal = palette.AsSpan(512, 256);

        var sumR = new int[256];
        var sumG = new int[256];
        var sumB = new int[256];
        var cntR = new int[256];
        var cntG = new int[256];
        var cntB = new int[256];

        for (var gy = 0; gy < gridSize; gy++)
        {
            ct.ThrowIfCancellationRequested();

            var y = (int)((gy + 0.5) * src.Height / gridSize);
            if (y >= src.Height) 
                y = src.Height - 1;

            var idxRow = index.Data.AsSpan(y * index.BytesPerRow, rowBytes);

            for (var gx = 0; gx < gridSize; gx++)
            {
                var x = (int)((gx + 0.5) * src.Width / gridSize);
                if (x >= src.Width) x = src.Width - 1;

                var off = x * bpcBytes;

                // Convert the index sample to 0..255
                var p = PsdSampleConvert.SampleTo8(idxRow, off, bpcBytes);

                var r0 = rPal[p];
                var g0 = gPal[p];
                var b0 = bPal[p];

                var rgb = IccTransformSampler.OracleIccRgb(config, r0, g0, b0);

                sumR[r0] += rgb.r;
                cntR[r0]++;

                sumG[g0] += rgb.g;
                cntG[g0]++;

                sumB[b0] += rgb.b;
                cntB[b0]++;
            }
        }

        return RgbCurveLutBuilder.BuildRgbCurves(sumR, cntR, sumG, cntG, sumB, cntB);
    }
}