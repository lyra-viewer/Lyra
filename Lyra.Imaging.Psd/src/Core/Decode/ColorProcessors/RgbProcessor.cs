using System.Buffers;
using Lyra.Imaging.Psd.Core.Common;
using Lyra.Imaging.Psd.Core.Decode.ColorCalibration;
using Lyra.Imaging.Psd.Core.Decode.Pixel;
using Lyra.Imaging.Psd.Core.SectionData;
using Wacton.Unicolour;

namespace Lyra.Imaging.Psd.Core.Decode.ColorProcessors;

public sealed class RgbProcessor : IColorModeProcessor
{
    public string? IccProfileUsed { get; private set; }

    private static readonly IccCalibrationProvider CalibrationProvider = new();

    public RgbaSurface Process(PlaneImage src, ColorModeContext ctx, ColorModeData? colorModeData, CancellationToken ct)
    {
        if (src.BitsPerChannel != 8)
            throw new NotSupportedException("RGB processor currently supports only 8 bits per channel.");

        var r = src.GetPlaneOrThrow(PlaneRole.R);
        var g = src.GetPlaneOrThrow(PlaneRole.G);
        var b = src.GetPlaneOrThrow(PlaneRole.B);
        src.TryGetPlane(PlaneRole.A, out var a);

        if (r.BytesPerRow < src.Width || g.BytesPerRow < src.Width || b.BytesPerRow < src.Width)
            throw new InvalidOperationException("RGB plane BytesPerRow is smaller than Width for 8bpc.");

        if (a.Data != null && a.BytesPerRow < src.Width)
            throw new InvalidOperationException("Alpha plane BytesPerRow is smaller than Width for 8bpc.");

        var stride = checked(src.Width * 4);
        var size = checked(stride * src.Height);

        const int gridSize = ColorCalibrationDefaults.GridSize;
        var calibration = CalibrationProvider.GetCalibration(
            new ColorCalibrationRequest(
                SourceColorMode: ColorMode.Rgb,
                EmbeddedIccProfile: ctx.IccProfile,
                PreferColorManagement: ctx.PreferColorManagement,
                GridSize: gridSize),
            config => BuildCalibrationLuts(src, r, g, b, config, gridSize, ct),
            out var iccProfileUsed
        );

        IccProfileUsed = iccProfileUsed;

        var useCalibration = ctx.PreferColorManagement && !calibration.IsIdentity;

        var owner = MemoryPool<byte>.Shared.Rent(size);
        var surface = new RgbaSurface(src.Width, src.Height, owner, stride, ctx.OutputFormat);

        try
        {
            for (var y = 0; y < src.Height; y++)
            {
                ct.ThrowIfCancellationRequested();

                var rRow = r.Data.AsSpan(y * r.BytesPerRow, src.Width);
                var gRow = g.Data.AsSpan(y * g.BytesPerRow, src.Width);
                var bRow = b.Data.AsSpan(y * b.BytesPerRow, src.Width);

                var dstRow = surface.GetRowSpan(y);

                if (a.Data != null)
                {
                    var aRow = a.Data.AsSpan(y * a.BytesPerRow, src.Width);
                    PixelRowWriter.WriteRgbRow(
                        dstRow,
                        ctx.OutputFormat.PixelFormat,
                        ctx.OutputFormat.AlphaType,
                        rRow, gRow, bRow,
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
                        rRow, gRow, bRow,
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
    }

    private static RgbLuts BuildCalibrationLuts(
        PlaneImage src,
        Plane r, Plane g, Plane b,
        Configuration config,
        int gridSize,
        CancellationToken ct)
    {
        var sumR = new int[256];
        var sumG = new int[256];
        var sumB = new int[256];
        var cntR = new int[256];
        var cntG = new int[256];
        var cntB = new int[256];

        for (var gy = 0; gy < gridSize; gy++)
        {
            ct.ThrowIfCancellationRequested();

            var row = (int)((gy + 0.5) * src.Height / gridSize);
            if (row >= src.Height) row = src.Height - 1;

            var rRow = r.Data.AsSpan(row * r.BytesPerRow, src.Width);
            var gRow = g.Data.AsSpan(row * g.BytesPerRow, src.Width);
            var bRow = b.Data.AsSpan(row * b.BytesPerRow, src.Width);

            for (var gx = 0; gx < gridSize; gx++)
            {
                var col = (int)((gx + 0.5) * src.Width / gridSize);
                if (col >= src.Width) col = src.Width - 1;

                var r0 = rRow[col];
                var g0 = gRow[col];
                var b0 = bRow[col];

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