using System.Buffers;
using System.Runtime.CompilerServices;
using Lyra.Imaging.Psd.Core.Common;
using Lyra.Imaging.Psd.Core.Decode.ColorCalibration;
using Lyra.Imaging.Psd.Core.Decode.ColorCalibration.Cmyk;
using Lyra.Imaging.Psd.Core.Decode.ColorCalibration.Rgb;
using Lyra.Imaging.Psd.Core.Decode.Pixel;
using Lyra.Imaging.Psd.Core.SectionData;
using Wacton.Unicolour;
using Wacton.Unicolour.Icc;

namespace Lyra.Imaging.Psd.Core.Decode.Color.Processors;

public sealed class CmykProcessor : IColorModeProcessor
{
    public string? IccProfileUsed { get; private set; }

    // Heuristic fallback tuning (used when ICC color management is disabled/unavailable).
    private const float OutputGamma = 0.95f;
    private const bool EnableNeutralBalance = true;

    // How "neutral" a pixel must be to qualify for neutral-balance correction (0..255).
    // Lower => affects fewer pixels (safer).
    private const int NeutralThreshold = 20;

    // Neutral-balance correction, applied only to near-neutrals.
    private const int NeutralRAdd = 8;
    private const int NeutralGAdd = -6;
    private const int NeutralBAdd = 0;

    // CMYK ICC path tuning.
    private const int GridSize = 17;
    private const bool GridInvert = true;
    private const Intent GridIntent = Intent.Perceptual;

    // Minimum row count for Parallel.For overhead.
    private const int ParallelRowThreshold = 64;

    private static readonly byte[] GammaLut = BuildGammaLut(OutputGamma);
    private static readonly CmykGridLutCache GridLutCache = new();

    public RgbaSurface Process(PlaneImage src, ColorModeContext ctx, ColorModeData? colorModeData, CancellationToken ct)
    {
        var depth = PsdDepthUtil.FromBitsPerChannel(src.BitsPerChannel);
        var bpcBytes = depth.BytesPerChannel();
        var srcRowBytes = depth.RowBytes(src.Width);

        var c = src.GetPlaneOrThrow(PlaneRole.C);
        var m = src.GetPlaneOrThrow(PlaneRole.M);
        var y = src.GetPlaneOrThrow(PlaneRole.Y);
        var k = src.GetPlaneOrThrow(PlaneRole.K);

        src.TryGetPlane(PlaneRole.A, out var a);

        var hasAlpha = a.Data is { Length: > 0 };

        if (c.BytesPerRow < srcRowBytes || m.BytesPerRow < srcRowBytes ||
            y.BytesPerRow < srcRowBytes || k.BytesPerRow < srcRowBytes)
            throw new InvalidOperationException("Plane BytesPerRow is smaller than Width*bpc for CMYK.");

        if (hasAlpha && a.BytesPerRow < srcRowBytes)
            throw new InvalidOperationException("Alpha plane BytesPerRow is smaller than Width*bpc.");

        var dstPixelFormat = ctx.OutputFormat.PixelFormat;
        var alphaType = ctx.OutputFormat.AlphaType;

        var stride = checked(src.Width * 4);
        var size = checked(stride * src.Height);

        var cmykGridLut = CmykGridLut.Identity;
        var useHeuristicTuning = true;

        if (ctx.PreferColorManagement)
        {
            Configuration? config = null;
            string? fallbackProfilePath = null;

            if (ctx.IccProfile is { Length: > 0 })
            {
                config = new Configuration(iccConfig: new IccConfiguration(ctx.IccProfile, GridIntent));
                IccProfileUsed = "Embedded ICC Profile";
            }
            else
            {
                fallbackProfilePath = SystemCmykProfileLocator.TryGetDefaultCmykIccPath();
                if (fallbackProfilePath is not null)
                {
                    config = new Configuration(iccConfig: new IccConfiguration(fallbackProfilePath, GridIntent));
                    IccProfileUsed = Path.GetFileNameWithoutExtension(fallbackProfilePath);
                }
            }

            if (config is not null)
            {
                cmykGridLut = GridLutCache.GetOrCreate(
                    ctx.IccProfile,
                    fallbackProfilePath,
                    GridIntent,
                    GridInvert,
                    GridSize,
                    () => BuildCmykGridLut(config, GridSize, GridInvert, ct));

                useHeuristicTuning = false;
            }
            else
            {
                IccProfileUsed = null;
            }
        }
        else
        {
            IccProfileUsed = null;
        }

        var owner = MemoryPool<byte>.Shared.Rent(size);
        var surface = new RgbaSurface(src.Width, src.Height, owner, stride, ctx.OutputFormat);

        try
        {
            if (useHeuristicTuning)
            {
                var tuning = new PixelRowWriter.CmykHeuristicTuning(
                    GammaLut,
                    EnableNeutralBalance,
                    NeutralThreshold,
                    NeutralRAdd,
                    NeutralGAdd,
                    NeutralBAdd);

                ProcessRowsHeuristic(
                    surface, src, c, m, y, k, a, hasAlpha,
                    bpcBytes, srcRowBytes,
                    dstPixelFormat, alphaType,
                    tuning, ct
                );
            }
            else
            {
                var lookupTables = new PixelRowWriter.CmykGridLookupTables(cmykGridLut.GridSize);

                ProcessRowsGridLut(
                    surface, src, c, m, y, k, a, hasAlpha,
                    bpcBytes, srcRowBytes,
                    dstPixelFormat, alphaType,
                    cmykGridLut, lookupTables, ct
                );
            }

            return surface;
        }
        catch
        {
            surface.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Process all rows using the heuristic (non-ICC) fallback path.
    /// Parallelizes across rows when the image is large enough.
    /// </summary>
    private static void ProcessRowsHeuristic(
        RgbaSurface surface,
        PlaneImage src,
        Plane c, Plane m, Plane y, Plane k, Plane a,
        bool hasAlpha,
        int bpcBytes, int srcRowBytes,
        PixelFormat dstPixelFormat, AlphaType alphaType,
        PixelRowWriter.CmykHeuristicTuning tuning,
        CancellationToken ct
    )
    {
        var height = src.Height;
        var width = src.Width;

        if (height >= ParallelRowThreshold)
        {
            Parallel.For(
                fromInclusive: 0,
                toExclusive: height,
                new ParallelOptions
                {
                    CancellationToken = ct,
                    MaxDegreeOfParallelism = Environment.ProcessorCount
                },
                () => bpcBytes != 1 ? new RowBuffers(width) : RowBuffers.Empty,
                (row, _, rowBuffers) =>
                {
                    ProcessSingleRowHeuristic(
                        surface, c, m, y, k, a, hasAlpha,
                        bpcBytes, srcRowBytes, width, row,
                        dstPixelFormat, alphaType,
                        in tuning, rowBuffers
                    );
                    return rowBuffers;
                },
                rowBuffers => rowBuffers.Return());
        }
        else
        {
            var rowBuffers = bpcBytes != 1 ? new RowBuffers(width) : RowBuffers.Empty;
            try
            {
                for (var row = 0; row < height; row++)
                {
                    ct.ThrowIfCancellationRequested();

                    ProcessSingleRowHeuristic(
                        surface, c, m, y, k, a, hasAlpha,
                        bpcBytes, srcRowBytes, width, row,
                        dstPixelFormat, alphaType,
                        in tuning, rowBuffers
                    );
                }
            }
            finally
            {
                rowBuffers.Return();
            }
        }
    }

    /// <summary>
    /// Process all rows using the ICC grid LUT path.
    /// Parallelizes across rows when the image is large enough.
    /// </summary>
    private static void ProcessRowsGridLut(
        RgbaSurface surface,
        PlaneImage src,
        Plane c, Plane m, Plane y, Plane k, Plane a,
        bool hasAlpha,
        int bpcBytes, int srcRowBytes,
        PixelFormat dstPixelFormat, AlphaType alphaType,
        CmykGridLut cmykGridLut,
        PixelRowWriter.CmykGridLookupTables lookupTables,
        CancellationToken ct
    )
    {
        var height = src.Height;
        var width = src.Width;

        if (height >= ParallelRowThreshold)
        {
            Parallel.For(
                fromInclusive: 0,
                toExclusive: height,
                new ParallelOptions
                {
                    CancellationToken = ct,
                    MaxDegreeOfParallelism = Environment.ProcessorCount
                },
                () => bpcBytes != 1 ? new RowBuffers(width) : RowBuffers.Empty,
                (row, _, rowBuffers) =>
                {
                    ProcessSingleRowGridLut(
                        surface, c, m, y, k, a, hasAlpha,
                        bpcBytes, srcRowBytes, width, row,
                        dstPixelFormat, alphaType,
                        cmykGridLut, lookupTables, rowBuffers
                    );
                    return rowBuffers;
                },
                rowBuffers => rowBuffers.Return());
        }
        else
        {
            var rowBuffers = bpcBytes != 1 ? new RowBuffers(width) : RowBuffers.Empty;
            try
            {
                for (var row = 0; row < height; row++)
                {
                    ct.ThrowIfCancellationRequested();

                    ProcessSingleRowGridLut(
                        surface, c, m, y, k, a, hasAlpha,
                        bpcBytes, srcRowBytes, width, row,
                        dstPixelFormat, alphaType,
                        cmykGridLut, lookupTables, rowBuffers
                    );
                }
            }
            finally
            {
                rowBuffers.Return();
            }
        }
    }

    private static void ProcessSingleRowHeuristic(
        RgbaSurface surface,
        Plane c, Plane m, Plane y, Plane k, Plane a,
        bool hasAlpha,
        int bpcBytes, int srcRowBytes, int width, int row,
        PixelFormat dstPixelFormat, AlphaType alphaType,
        in PixelRowWriter.CmykHeuristicTuning tuning,
        RowBuffers buffers
    )
    {
        GetRow8(c, m, y, k, a, hasAlpha, bpcBytes, srcRowBytes, width, row, buffers,
            out var cRow8,
            out var mRow8,
            out var yRow8,
            out var kRow8,
            out var aRow8
        );

        var dstRow = surface.GetRowSpan(row);

        PixelRowWriter.WriteCmykRow(
            dstRow,
            dstPixelFormat,
            alphaType,
            cRow8, mRow8, yRow8, kRow8,
            hasAlpha ? aRow8 : default,
            hasAlpha,
            useHeuristicTuning: true,
            tuning,
            RgbLuts.Identity
        );
    }

    private static void ProcessSingleRowGridLut(
        RgbaSurface surface,
        Plane c, Plane m, Plane y, Plane k, Plane a,
        bool hasAlpha,
        int bpcBytes, int srcRowBytes, int width, int row,
        PixelFormat dstPixelFormat, AlphaType alphaType,
        CmykGridLut cmykGridLut,
        PixelRowWriter.CmykGridLookupTables lookupTables,
        RowBuffers buffers
    )
    {
        GetRow8(c, m, y, k, a, hasAlpha, bpcBytes, srcRowBytes, width, row, buffers,
            out var cRow8, out var mRow8, out var yRow8, out var kRow8, out var aRow8);

        var dstRow = surface.GetRowSpan(row);

        PixelRowWriter.WriteCmykRowGridLut(
            dstRow,
            dstPixelFormat,
            alphaType,
            cRow8, mRow8, yRow8, kRow8,
            hasAlpha ? aRow8 : default,
            hasAlpha,
            cmykGridLut,
            lookupTables
        );
    }

    /// <summary>
    /// Extract 8-bit row data from the planes, converting from 16-bit or 32-bit if necessary.
    /// </summary>
    private static void GetRow8(
        Plane c, Plane m, Plane y, Plane k, Plane a,
        bool hasAlpha,
        int bpcBytes, int srcRowBytes, int width, int row,
        RowBuffers buffers,
        out ReadOnlySpan<byte> cRow8,
        out ReadOnlySpan<byte> mRow8,
        out ReadOnlySpan<byte> yRow8,
        out ReadOnlySpan<byte> kRow8,
        out ReadOnlySpan<byte> aRow8
    )
    {
        aRow8 = default;

        if (bpcBytes == 1)
        {
            cRow8 = c.Data.AsSpan(row * c.BytesPerRow, width);
            mRow8 = m.Data.AsSpan(row * m.BytesPerRow, width);
            yRow8 = y.Data.AsSpan(row * y.BytesPerRow, width);
            kRow8 = k.Data.AsSpan(row * k.BytesPerRow, width);

            if (hasAlpha)
                aRow8 = a.Data.AsSpan(row * a.BytesPerRow, width);
        }
        else
        {
            var cc = buffers.C.AsSpan(0, width);
            var mm = buffers.M.AsSpan(0, width);
            var yy = buffers.Y.AsSpan(0, width);
            var kk = buffers.K.AsSpan(0, width);

            var cRowRaw = c.Data.AsSpan(row * c.BytesPerRow, srcRowBytes);
            var mRowRaw = m.Data.AsSpan(row * m.BytesPerRow, srcRowBytes);
            var yRowRaw = y.Data.AsSpan(row * y.BytesPerRow, srcRowBytes);
            var kRowRaw = k.Data.AsSpan(row * k.BytesPerRow, srcRowBytes);

            if (bpcBytes == 2)
            {
                PsdSampleConvert.Row16BeTo8(cRowRaw, cc);
                PsdSampleConvert.Row16BeTo8(mRowRaw, mm);
                PsdSampleConvert.Row16BeTo8(yRowRaw, yy);
                PsdSampleConvert.Row16BeTo8(kRowRaw, kk);

                if (hasAlpha)
                {
                    var aRaw = a.Data.AsSpan(row * a.BytesPerRow, srcRowBytes);
                    var aa = buffers.A!.AsSpan(0, width);
                    PsdSampleConvert.Row16BeTo8(aRaw, aa);
                    aRow8 = aa;
                }
            }
            else
            {
                PsdSampleConvert.Row32FloatBeTo8(cRowRaw, cc);
                PsdSampleConvert.Row32FloatBeTo8(mRowRaw, mm);
                PsdSampleConvert.Row32FloatBeTo8(yRowRaw, yy);
                PsdSampleConvert.Row32FloatBeTo8(kRowRaw, kk);

                if (hasAlpha)
                {
                    var aRaw = a.Data.AsSpan(row * a.BytesPerRow, srcRowBytes);
                    var aa = buffers.A!.AsSpan(0, width);
                    PsdSampleConvert.Row32FloatBeTo8(aRaw, aa);
                    aRow8 = aa;
                }
            }

            cRow8 = cc;
            mRow8 = mm;
            yRow8 = yy;
            kRow8 = kk;
        }
    }

    /// <summary>
    /// Per-thread rented buffers for depth conversion. Avoids allocation in the hot loop.
    /// </summary>
    private sealed class RowBuffers
    {
        public static readonly RowBuffers Empty = new();

        public byte[]? C { get; }
        public byte[]? M { get; }
        public byte[]? Y { get; }
        public byte[]? K { get; }
        public byte[]? A { get; }

        private bool _rented;

        private RowBuffers()
        {
            _rented = false;
        }

        public RowBuffers(int width)
        {
            C = ArrayPool<byte>.Shared.Rent(width);
            M = ArrayPool<byte>.Shared.Rent(width);
            Y = ArrayPool<byte>.Shared.Rent(width);
            K = ArrayPool<byte>.Shared.Rent(width);
            A = ArrayPool<byte>.Shared.Rent(width);
            _rented = true;
        }

        public void Return()
        {
            if (!_rented)
                return;

            _rented = false;

            if (C != null)
                ArrayPool<byte>.Shared.Return(C);

            if (M != null)
                ArrayPool<byte>.Shared.Return(M);

            if (Y != null)
                ArrayPool<byte>.Shared.Return(Y);

            if (K != null)
                ArrayPool<byte>.Shared.Return(K);

            if (A != null)
                ArrayPool<byte>.Shared.Return(A);
        }
    }

    // Black point compensation: matches ColorSync / lcms2 / Adobe ACE behavior.
    // Strength: 0.0 = no compensation (raw ICC output),
    //           1.0 = full compensation (black -> 0,0,0),
    //           0.5 = half correction (useful if full BPC crushes shadow detail).
    private const float BlackPointCompensationStrength = .6f;

    private static CmykGridLut BuildCmykGridLut(Configuration config, int gridSize, bool invert, CancellationToken ct)
    {
        var totalPoints = checked(gridSize * gridSize * gridSize * gridSize);
        var samples = new byte[checked(totalPoints * 3)];

        // Phase 1: Sample all grid points
        Parallel.For(
            fromInclusive: 0,
            toExclusive: totalPoints,
            new ParallelOptions
            {
                CancellationToken = ct,
                MaxDegreeOfParallelism = Environment.ProcessorCount
            },
            index =>
            {
                UnflattenIndex(index, gridSize, out var ic, out var im, out var iy, out var ik);

                byte c = GridToByte(ic, gridSize);
                byte m = GridToByte(im, gridSize);
                byte y = GridToByte(iy, gridSize);
                byte k = GridToByte(ik, gridSize);

                var rgb = IccTransformSampler.OracleIccRgb(config, c, m, y, k, invert);

                int baseIdx = index * 3;
                samples[baseIdx + 0] = (byte)rgb.r;
                samples[baseIdx + 1] = (byte)rgb.g;
                samples[baseIdx + 2] = (byte)rgb.b;
            });

        // Phase 2: Black point compensation
        if (BlackPointCompensationStrength > 0f)
        {
            ct.ThrowIfCancellationRequested();
            ApplyBlackPointCompensation(config, invert, samples, BlackPointCompensationStrength);
        }

        return new CmykGridLut(gridSize, invert, samples);
    }

    /// <summary>
    /// Measures the ICC profile's black and white points, then linearly remaps all LUT
    /// samples per-channel. This is the same operation that ColorSync and lcms2 call "Black Point Compensation".
    /// <paramref name="strength"/> controls how much of the measured black offset is removed:
    /// 1.0 = full (black maps to 0), 0.5 = half, 0.0 = none.
    /// </summary>
    private static void ApplyBlackPointCompensation(Configuration config, bool invert, byte[] samples, float strength)
    {
        byte noInk = 255;
        var white = IccTransformSampler.OracleIccRgb(config, noInk, noInk, noInk, noInk, invert);

        byte fullInk = 0; // PSD convention: 0 = full ink
        var kOnlyBlack = IccTransformSampler.OracleIccRgb(config, noInk, noInk, noInk, fullInk, invert);
        var richBlack = IccTransformSampler.OracleIccRgb(config, fullInk, fullInk, fullInk, fullInk, invert);

        float rawBlackR = Math.Min(kOnlyBlack.r, richBlack.r);
        float rawBlackG = Math.Min(kOnlyBlack.g, richBlack.g);
        float rawBlackB = Math.Min(kOnlyBlack.b, richBlack.b);

        int blackR = (int)(rawBlackR * Math.Clamp(strength, 0f, 1f) + 0.5f);
        int blackG = (int)(rawBlackG * Math.Clamp(strength, 0f, 1f) + 0.5f);
        int blackB = (int)(rawBlackB * Math.Clamp(strength, 0f, 1f) + 0.5f);

        int whiteR = white.r;
        int whiteG = white.g;
        int whiteB = white.b;

        // Only apply if there's actual range compression to fix.
        int rangeR = whiteR - blackR;
        int rangeG = whiteG - blackG;
        int rangeB = whiteB - blackB;

        // If range is too small or inverted, skip BPC to avoid division issues.
        if (rangeR < 10 || rangeG < 10 || rangeB < 10)
            return;

        int halfR = rangeR / 2;
        int halfG = rangeG / 2;
        int halfB = rangeB / 2;

        for (int i = 0; i < samples.Length; i += 3)
        {
            samples[i + 0] = RemapChannel(samples[i + 0], blackR, rangeR, halfR);
            samples[i + 1] = RemapChannel(samples[i + 1], blackG, rangeG, halfG);
            samples[i + 2] = RemapChannel(samples[i + 2], blackB, rangeB, halfB);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static byte RemapChannel(byte raw, int black, int range, int half)
    {
        int shifted = raw - black;
        if (shifted <= 0)
            return 0;

        int result = (shifted * 255 + half) / range;
        return result >= 255 ? (byte)255 : (byte)result;
    }

    private static void UnflattenIndex(int index, int gridSize, out int ic, out int im, out int iy, out int ik)
    {
        ik = index % gridSize;
        index /= gridSize;

        iy = index % gridSize;
        index /= gridSize;

        im = index % gridSize;
        index /= gridSize;

        ic = index;
    }

    private static byte GridToByte(int i, int gridSize)
    {
        if (i <= 0)
            return 0;

        if (i >= gridSize - 1)
            return 255;

        // Symmetric rounding that works for both odd and even grid sizes.
        return (byte)((2 * i * 255 + gridSize - 1) / (2 * (gridSize - 1)));
    }

    private static byte[] BuildGammaLut(float gamma)
    {
        var lut = new byte[256];
        for (var i = 0; i < 256; i++)
        {
            var x = i / 255f;
            var y = MathF.Pow(x, gamma);
            var v = (int)(y * 255f + 0.5f);
            if (v < 0)
                v = 0;

            if (v > 255)
                v = 255;

            lut[i] = (byte)v;
        }

        return lut;
    }
}