using System.Buffers;
using Lyra.Imaging.Psd.Core.Common;
using Lyra.Imaging.Psd.Core.Decode.ColorCalibration;
using Lyra.Imaging.Psd.Core.Decode.Pixel;
using Lyra.Imaging.Psd.Core.SectionData;
using Wacton.Unicolour;

namespace Lyra.Imaging.Psd.Core.Decode.ColorProcessors;

public sealed class GrayscaleProcessor : IColorModeProcessor
{
    public string? IccProfileUsed { get; private set; }

    private static readonly IccCalibrationProvider CalibrationProvider = new();

    public RgbaSurface Process(PlaneImage src, ColorModeContext ctx, ColorModeData? colorModeData, CancellationToken ct)
    {
        var depth = PsdDepthUtil.FromBitsPerChannel(src.BitsPerChannel);
        var bpcBytes = depth.BytesPerChannel();

        var gray = src.GetPlaneOrThrow(PlaneRole.Gray);
        src.TryGetPlane(PlaneRole.A, out var a);

        var rowBytes = depth.RowBytes(src.Width);

        if (gray.BytesPerRow < rowBytes)
            throw new InvalidOperationException("Gray plane BytesPerRow is smaller than Width*bpc.");

        if (a.Data != null && a.BytesPerRow < rowBytes)
            throw new InvalidOperationException("Alpha plane BytesPerRow is smaller than Width*bpc.");

        var stride = checked(src.Width * 4);
        var size = checked(stride * src.Height);

        const int gridSize = ColorCalibrationDefaults.GridSize;

        var calibration = CalibrationProvider.GetCalibration(
            new ColorCalibrationRequest(
                SourceColorMode: ColorMode.Grayscale,
                EmbeddedIccProfile: ctx.IccProfile,
                PreferColorManagement: ctx.PreferColorManagement,
                GridSize: gridSize),
            config => BuildGrayAsRgbCalibrationLuts(src, gray, config, gridSize, ct),
            out var iccProfileUsed);

        IccProfileUsed = iccProfileUsed;

        var useCalibration = ctx.PreferColorManagement && !calibration.IsIdentity;

        var owner = MemoryPool<byte>.Shared.Rent(size);
        var surface = new RgbaSurface(src.Width, src.Height, owner, stride, ctx.OutputFormat);

        byte[]? g8Rent = null;
        byte[]? a8Rent = null;

        if (bpcBytes != 1)
        {
            g8Rent = ArrayPool<byte>.Shared.Rent(src.Width);
            if (a.Data != null && a.Data.Length != 0)
                a8Rent = ArrayPool<byte>.Shared.Rent(src.Width);
        }

        try
        {
            for (var y = 0; y < src.Height; y++)
            {
                ct.ThrowIfCancellationRequested();

                var gRowRaw = gray.Data.AsSpan(y * gray.BytesPerRow, rowBytes);

                ReadOnlySpan<byte> gRow8;
                ReadOnlySpan<byte> aRow8 = default;

                var hasAlpha = a.Data != null && a.Data.Length != 0;

                if (bpcBytes == 1)
                {
                    gRow8 = gRowRaw[..src.Width];

                    if (hasAlpha)
                        aRow8 = a.Data.AsSpan(y * a.BytesPerRow, src.Width);
                }
                else
                {
                    var gg = g8Rent!.AsSpan(0, src.Width);

                    if (bpcBytes == 2)
                    {
                        PsdSampleConvert.Row16BeTo8(gg, gRowRaw);

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
                        PsdSampleConvert.Row32FloatBeTo8(gg, gRowRaw);

                        if (hasAlpha)
                        {
                            var aRaw = a.Data.AsSpan(y * a.BytesPerRow, rowBytes);
                            var aa = a8Rent!.AsSpan(0, src.Width);
                            PsdSampleConvert.Row32FloatBeTo8(aa, aRaw);
                            aRow8 = aa;
                        }
                    }

                    gRow8 = gg;
                }

                var dstRow = surface.GetRowSpan(y);

                PixelRowWriter.WriteGrayRow(
                    dstRow,
                    ctx.OutputFormat.PixelFormat,
                    ctx.OutputFormat.AlphaType,
                    gRow8,
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
            if (g8Rent != null) ArrayPool<byte>.Shared.Return(g8Rent);
            if (a8Rent != null) ArrayPool<byte>.Shared.Return(a8Rent);
        }
    }

    private static RgbLuts BuildGrayAsRgbCalibrationLuts(PlaneImage src, Plane gray, Configuration config, int gridSize, CancellationToken ct)
    {
        var depth = PsdDepthUtil.FromBitsPerChannel(src.BitsPerChannel);
        var bpcBytes = depth.BytesPerChannel();
        var rowBytes = depth.RowBytes(src.Width);

        var sumR = new int[256];
        var sumG = new int[256];
        var sumB = new int[256];
        var cnt = new int[256];

        for (var gy = 0; gy < gridSize; gy++)
        {
            ct.ThrowIfCancellationRequested();

            var row = (int)((gy + 0.5) * src.Height / gridSize);
            if (row >= src.Height)
                row = src.Height - 1;

            var gRow = gray.Data.AsSpan(row * gray.BytesPerRow, rowBytes);

            for (var gx = 0; gx < gridSize; gx++)
            {
                var col = (int)((gx + 0.5) * src.Width / gridSize);
                if (col >= src.Width)
                    col = src.Width - 1;

                var off = col * bpcBytes;
                var g0 = PsdSampleConvert.SampleTo8(gRow, off, bpcBytes);

                var rgb = IccOracle.OracleIccRgb(config, g0, g0, g0);

                sumR[g0] += rgb.r;
                sumG[g0] += rgb.g;
                sumB[g0] += rgb.b;
                cnt[g0]++;
            }
        }

        return LutBuilder.BuildRgbCurves(sumR, cnt, sumG, cnt, sumB, cnt);
    }
}