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
        var depth = PsdDepthUtil.FromBitsPerChannel(src.BitsPerChannel);
        var bpcBytes = depth.BytesPerChannel();

        var r = src.GetPlaneOrThrow(PlaneRole.R);
        var g = src.GetPlaneOrThrow(PlaneRole.G);
        var b = src.GetPlaneOrThrow(PlaneRole.B);
        src.TryGetPlane(PlaneRole.A, out var a);

        var rowBytes = depth.RowBytes(src.Width);

        if (r.BytesPerRow < rowBytes || g.BytesPerRow < rowBytes || b.BytesPerRow < rowBytes)
            throw new InvalidOperationException("RGB plane BytesPerRow is smaller than Width*bpc.");

        if (a.Data != null && a.BytesPerRow < rowBytes)
            throw new InvalidOperationException("Alpha plane BytesPerRow is smaller than Width*bpc.");

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
            out var iccProfileUsed);

        IccProfileUsed = iccProfileUsed;

        var useCalibration = ctx.PreferColorManagement && !calibration.IsIdentity;

        var owner = MemoryPool<byte>.Shared.Rent(size);
        var surface = new RgbaSurface(src.Width, src.Height, owner, stride, ctx.OutputFormat);

        byte[]? r8Rent = null;
        byte[]? g8Rent = null;
        byte[]? b8Rent = null;
        byte[]? a8Rent = null;

        if (bpcBytes != 1)
        {
            r8Rent = ArrayPool<byte>.Shared.Rent(src.Width);
            g8Rent = ArrayPool<byte>.Shared.Rent(src.Width);
            b8Rent = ArrayPool<byte>.Shared.Rent(src.Width);
            if (a.Data != null)
                a8Rent = ArrayPool<byte>.Shared.Rent(src.Width);
        }

        try
        {
            for (var y = 0; y < src.Height; y++)
            {
                ct.ThrowIfCancellationRequested();

                var rRowRaw = r.Data.AsSpan(y * r.BytesPerRow, rowBytes);
                var gRowRaw = g.Data.AsSpan(y * g.BytesPerRow, rowBytes);
                var bRowRaw = b.Data.AsSpan(y * b.BytesPerRow, rowBytes);

                ReadOnlySpan<byte> rRow8;
                ReadOnlySpan<byte> gRow8;
                ReadOnlySpan<byte> bRow8;
                ReadOnlySpan<byte> aRow8 = default;

                var hasAlpha = a.Data != null;

                if (bpcBytes == 1)
                {
                    rRow8 = rRowRaw[..src.Width];
                    gRow8 = gRowRaw[..src.Width];
                    bRow8 = bRowRaw[..src.Width];

                    if (hasAlpha)
                        aRow8 = a.Data.AsSpan(y * a.BytesPerRow, src.Width);
                }
                else
                {
                    var rr = r8Rent!.AsSpan(0, src.Width);
                    var gg = g8Rent!.AsSpan(0, src.Width);
                    var bb = b8Rent!.AsSpan(0, src.Width);

                    if (bpcBytes == 2)
                    {
                        PsdSampleConvert.Row16BeTo8(rr, rRowRaw);
                        PsdSampleConvert.Row16BeTo8(gg, gRowRaw);
                        PsdSampleConvert.Row16BeTo8(bb, bRowRaw);

                        if (hasAlpha)
                        {
                            var aRaw = a.Data.AsSpan(y * a.BytesPerRow, rowBytes);
                            var aa = a8Rent!.AsSpan(0, src.Width);
                            PsdSampleConvert.Row16BeTo8(aa, aRaw);
                            aRow8 = aa;
                        }
                    }
                    else
                    {
                        PsdSampleConvert.Row32FloatBeTo8(rr, rRowRaw);
                        PsdSampleConvert.Row32FloatBeTo8(gg, gRowRaw);
                        PsdSampleConvert.Row32FloatBeTo8(bb, bRowRaw);

                        if (hasAlpha)
                        {
                            var aRaw = a.Data.AsSpan(y * a.BytesPerRow, rowBytes);
                            var aa = a8Rent!.AsSpan(0, src.Width);
                            PsdSampleConvert.Row32FloatBeTo8(aa, aRaw);
                            aRow8 = aa;
                        }
                    }

                    rRow8 = rr;
                    gRow8 = gg;
                    bRow8 = bb;
                }

                var dstRow = surface.GetRowSpan(y);

                PixelRowWriter.WriteRgbRow(
                    dstRow,
                    ctx.OutputFormat.PixelFormat,
                    ctx.OutputFormat.AlphaType,
                    rRow8,
                    gRow8,
                    bRow8,
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
            if (r8Rent != null) ArrayPool<byte>.Shared.Return(r8Rent);
            if (g8Rent != null) ArrayPool<byte>.Shared.Return(g8Rent);
            if (b8Rent != null) ArrayPool<byte>.Shared.Return(b8Rent);
            if (a8Rent != null) ArrayPool<byte>.Shared.Return(a8Rent);
        }
    }

    private static RgbLuts BuildCalibrationLuts(
        PlaneImage src,
        Plane r, Plane g, Plane b,
        Configuration config,
        int gridSize,
        CancellationToken ct)
    {
        var depth = PsdDepthUtil.FromBitsPerChannel(src.BitsPerChannel);
        var bpcBytes = depth.BytesPerChannel();
        var rowBytes = depth.RowBytes(src.Width);

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

            var rRow = r.Data.AsSpan(row * r.BytesPerRow, rowBytes);
            var gRow = g.Data.AsSpan(row * g.BytesPerRow, rowBytes);
            var bRow = b.Data.AsSpan(row * b.BytesPerRow, rowBytes);

            for (var gx = 0; gx < gridSize; gx++)
            {
                var col = (int)((gx + 0.5) * src.Width / gridSize);
                if (col >= src.Width) col = src.Width - 1;

                var off = col * bpcBytes;

                var r0 = PsdSampleConvert.SampleTo8(rRow, off, bpcBytes);
                var g0 = PsdSampleConvert.SampleTo8(gRow, off, bpcBytes);
                var b0 = PsdSampleConvert.SampleTo8(bRow, off, bpcBytes);

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