using System.Buffers;
using Lyra.Imaging.Psd.Core.Common;
using Lyra.Imaging.Psd.Core.Decode.ColorCalibration;
using Lyra.Imaging.Psd.Core.Decode.Pixel;
using Lyra.Imaging.Psd.Core.SectionData;
using Wacton.Unicolour;

namespace Lyra.Imaging.Psd.Core.Decode.ColorProcessors;

public sealed class IndexedProcessor : IColorModeProcessor
{
    public string? IccProfileUsed { get; private set; }

    private static readonly IccCalibrationProvider CalibrationProvider = new();

    public RgbaSurface Process(PlaneImage src, ColorModeContext ctx, ColorModeData? colorModeData, CancellationToken ct)
    {
        if (src.BitsPerChannel != 8)
            throw new NotSupportedException("Indexed color mode supports only 8 bits per channel.");

        var index = src.GetPlaneOrThrow(PlaneRole.Index);
        src.TryGetPlane(PlaneRole.A, out var alpha);

        if (index.BytesPerRow < src.Width)
            throw new InvalidOperationException("Index plane BytesPerRow is smaller than Width for 8bpc Indexed.");

        if (alpha.Data != null && alpha.BytesPerRow < src.Width)
            throw new InvalidOperationException("Alpha plane BytesPerRow is smaller than Width for 8bpc.");

        var palette = ctx.IndexedPaletteRgb ?? colorModeData?.Data;
        if (palette == null || palette.Length < 768)
            throw new InvalidOperationException("Indexed color mode requires a 768-byte RGB palette.");

        var rPal = palette.AsSpan(0, 256);
        var gPal = palette.AsSpan(256, 256);
        var bPal = palette.AsSpan(512, 256);

        var stride = checked(src.Width * 4);
        var size = checked(stride * src.Height);

        // ICC calibration (treat Indexed as RGB after palette expansion)
        const int gridSize = ColorCalibrationDefaults.GridSize;
        var calibration = CalibrationProvider.GetCalibration(
            new ColorCalibrationRequest(
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

        var rRowBuf = ArrayPool<byte>.Shared.Rent(src.Width);
        var gRowBuf = ArrayPool<byte>.Shared.Rent(src.Width);
        var bRowBuf = ArrayPool<byte>.Shared.Rent(src.Width);

        try
        {
            for (var y = 0; y < src.Height; y++)
            {
                ct.ThrowIfCancellationRequested();

                var idxRow = index.Data.AsSpan(y * index.BytesPerRow, src.Width);

                var rRow = rRowBuf.AsSpan(0, src.Width);
                var gRow = gRowBuf.AsSpan(0, src.Width);
                var bRow = bRowBuf.AsSpan(0, src.Width);

                for (var x = 0; x < src.Width; x++)
                {
                    var p = idxRow[x];
                    rRow[x] = rPal[p];
                    gRow[x] = gPal[p];
                    bRow[x] = bPal[p];
                }

                var dstRow = surface.GetRowSpan(y);

                if (alpha.Data != null)
                {
                    var aRow = alpha.Data.AsSpan(y * alpha.BytesPerRow, src.Width);
                    PixelRowWriter.WriteRgbRow(
                        dstRow,
                        ctx.OutputFormat.PixelFormat,
                        ctx.OutputFormat.AlphaType,
                        rRow,
                        gRow,
                        bRow,
                        aRow,
                        hasAlpha: true,
                        useCalibration,
                        calibration);
                }
                else
                {
                    PixelRowWriter.WriteRgbRow(
                        dstRow,
                        ctx.OutputFormat.PixelFormat,
                        ctx.OutputFormat.AlphaType,
                        rRow,
                        gRow,
                        bRow,
                        default,
                        hasAlpha: false,
                        useCalibration,
                        calibration);
                }
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
            if (y >= src.Height) y = src.Height - 1;

            var idxRow = index.Data.AsSpan(y * index.BytesPerRow, src.Width);

            for (var gx = 0; gx < gridSize; gx++)
            {
                var x = (int)((gx + 0.5) * src.Width / gridSize);
                if (x >= src.Width) x = src.Width - 1;

                var p = idxRow[x];

                var r0 = rPal[p];
                var g0 = gPal[p];
                var b0 = bPal[p];

                var rgb = IccOracle.OracleIccRgb(config, r0, g0, b0);

                sumR[r0] += rgb.r;
                cntR[r0]++;

                sumG[g0] += rgb.g;
                cntG[g0]++;

                sumB[b0] += rgb.b;
                cntB[b0]++;
            }
        }

        return LutBuilder.BuildRgbCurves(sumR, cntR, sumG, cntG, sumB, cntB);
    }
}
