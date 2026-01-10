using System.Buffers;
using Lyra.Imaging.Psd.Core.Common;
using Lyra.Imaging.Psd.Core.Decode.Color.ColorCalibration;
using Lyra.Imaging.Psd.Core.Decode.Pixel;
using Wacton.Unicolour;
using Wacton.Unicolour.Icc;

namespace Lyra.Imaging.Psd.Core.Decode.Color;

public sealed class RgbProcessor : IColorModeProcessor
{
    public string? IccProfileUsed { get; private set; }
    
    private static readonly IccCalibrationProvider CalibrationProvider = new();

    public RgbaSurface Process(PlaneImage src, ColorModeContext ctx, CancellationToken ct)
    {
        if (src.BitsPerChannel != 8)
            throw new NotSupportedException($"RGB processor currently supports only 8 bits/channel, got {src.BitsPerChannel}.");

        var r = src.GetPlaneOrThrow(PlaneRole.R);
        var g = src.GetPlaneOrThrow(PlaneRole.G);
        var b = src.GetPlaneOrThrow(PlaneRole.B);

        // Alpha is optional.
        src.TryGetPlane(PlaneRole.A, out var a);

        if (r.BytesPerRow < src.Width || g.BytesPerRow < src.Width || b.BytesPerRow < src.Width)
            throw new InvalidOperationException("Plane BytesPerRow is smaller than Width for 8bpc RGB.");

        if (a.Data != null && a.BytesPerRow < src.Width)
            throw new InvalidOperationException("Alpha plane BytesPerRow is smaller than Width for 8bpc.");

        var stride = checked(src.Width * 4);
        var size = checked(stride * src.Height);

        // ICC calibration (only meaningful when an embedded ICC profile is present for RGB).
        // If PreferColorManagement is false, or profile cannot be resolved, this becomes identity.
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

        var useCalibration = ctx.PreferColorManagement && !calibration.IsIdentity;

        IccProfileUsed = iccProfileUsed;
        
        // Output allocation.
        var dstPixelFormat = ctx.OutputFormat.PixelFormat;
        var alphaType = ctx.OutputFormat.AlphaType;

        var owner = MemoryPool<byte>.Shared.Rent(size);
        var surface = new RgbaSurface(src.Width, src.Height, owner, stride);

        try
        {
            for (var y = 0; y < src.Height; y++)
            {
                ct.ThrowIfCancellationRequested();

                var rRow = r.Data.AsSpan(y * r.BytesPerRow, src.Width);
                var gRow = g.Data.AsSpan(y * g.BytesPerRow, src.Width);
                var bRow = b.Data.AsSpan(y * b.BytesPerRow, src.Width);

                Span<byte> aRow = default;
                var hasAlpha = a.Data != null && a.Data.Length != 0;
                if (hasAlpha)
                    aRow = a.Data.AsSpan(y * a.BytesPerRow, src.Width);

                var dstRow = surface.GetRowSpan(y);

                PixelRowWriter.WriteRgbRow(
                    dstRow,
                    dstPixelFormat,
                    alphaType,
                    rRow,
                    gRow,
                    bRow,
                    hasAlpha ? aRow : default,
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

                var rgb = OracleIccRgb(config, r0, g0, b0);

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

    private static (int r, int g, int b) OracleIccRgb(Configuration config, byte r, byte g, byte b)
    {
        var r0 = r / 255.0;
        var g0 = g / 255.0;
        var b0 = b / 255.0;

        var u = new Unicolour(config, new Channels(r0, g0, b0));
        var rgb = u.Rgb.Byte255;

        return (Clamp0To255(rgb.R), Clamp0To255(rgb.G), Clamp0To255(rgb.B));

        static int Clamp0To255(int v) => v < 0 ? 0 : v > 255 ? 255 : v;
    }
}