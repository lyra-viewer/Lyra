using System.Buffers.Binary;
using Lyra.ManagedCodecs.Raster.Tiff;

namespace Lyra.ManagedCodecs.Raster.Dng;

/// <summary>
/// The parts of a DNG's camera profile that say how the maker renders the image: a tone curve
/// and a local tone map. Apple ProRAW carries both. Either may be absent.
/// </summary>
public sealed class DngRenderingProfile
{
    public DngToneCurve? ToneCurve { get; init; }
    public DngGainTableMap? GainTableMap { get; init; }

    public const ushort TagProfileToneCurve = 50940;
    public const ushort TagProfileGainTableMap = 52525;
    public const ushort TagProfileGainTableMap2 = 52544;

    /// <summary>Largest tag payload read: Apple's map is 49 KB, and the format allows far more.</summary>
    private const int MaxPayloadBytes = 4 * 1024 * 1024;

    /// <summary>The profile's rendering tags, or null when the stream is not a TIFF or carries neither.</summary>
    public static DngRenderingProfile? Read(Stream stream)
    {
        if (TiffDirectoryReader.Read(stream) is not { } layout)
            return null;

        var directories = layout.AllDirectories().ToList();

        DngToneCurve? curve = null;
        if (First(directories, TagProfileToneCurve) is { } curveEntry
            && TiffDirectoryReader.ReadFloats(stream, curveEntry, layout.LittleEndian, DngToneCurve.MaxPoints * 2) is { } points)
            curve = DngToneCurve.FromPairs(points);

        // Version 2 takes precedence when both are present.
        DngGainTableMap? map = null;
        if (First(directories, TagProfileGainTableMap2) is { } v2 && TiffDirectoryReader.ReadBytes(stream, v2, MaxPayloadBytes) is { } v2Bytes)
            map = DngGainTableMap.Parse(v2Bytes, layout.LittleEndian, version2: true);

        if (map is null && First(directories, TagProfileGainTableMap) is { } v1 && TiffDirectoryReader.ReadBytes(stream, v1, MaxPayloadBytes) is { } v1Bytes)
            map = DngGainTableMap.Parse(v1Bytes, layout.LittleEndian, version2: false);

        return curve is null && map is null ? null : new DngRenderingProfile { ToneCurve = curve, GainTableMap = map };
    }

    private static TiffEntry? First(List<TiffDirectory> directories, ushort tag)
    {
        foreach (var directory in directories)
            if (directory.Find(tag) is { } entry)
                return entry;

        return null;
    }
}

/// <summary>
/// ProfileToneCurve: linear in, linear out, from (0, 0) to (1, 1), interpolated with a cubic
/// spline as the specification asks. Held as a dense table.
/// </summary>
public sealed class DngToneCurve
{
    public const int MaxPoints = 8192;

    public const int TableSize = 1024;

    public float[] Table { get; }

    public int PointCount { get; }

    private DngToneCurve(float[] table, int pointCount)
    {
        Table = table;
        PointCount = pointCount;
    }

    /// <summary>The curve from (x, y) pairs, or null when they do not describe one.</summary>
    public static DngToneCurve? FromPairs(float[] pairs)
    {
        if (pairs.Length < 4 || pairs.Length % 2 != 0)
            return null;

        var n = pairs.Length / 2;
        var x = new double[n];
        var y = new double[n];

        for (var i = 0; i < n; i++)
        {
            x[i] = pairs[2 * i];
            y[i] = pairs[2 * i + 1];

            if (!double.IsFinite(x[i]) || !double.IsFinite(y[i]) || x[i] < 0 || x[i] > 1 || y[i] < 0 || y[i] > 1)
                return null;

            if (i > 0 && x[i] <= x[i - 1])
                return null;
        }

        var slopes = SplineSecondDerivatives(x, y);
        var table = new float[TableSize];

        var segment = 0;
        for (var s = 0; s < TableSize; s++)
        {
            var t = s / (double)(TableSize - 1);
            if (t <= x[0])
            {
                table[s] = (float)y[0];
                continue;
            }

            if (t >= x[n - 1])
            {
                table[s] = (float)y[n - 1];
                continue;
            }

            while (segment < n - 2 && t > x[segment + 1])
                segment++;

            table[s] = (float)Math.Clamp(Spline(x, y, slopes, segment, t), 0, 1);
        }

        return new DngToneCurve(table, n);
    }

    public float Evaluate(float value)
    {
        if (float.IsNaN(value) || value <= 0f)
            return Table[0];

        if (value >= 1f)
            return Table[^1];

        var position = value * (TableSize - 1);
        var i = (int)position;
        var f = position - i;
        return Table[i] + (Table[Math.Min(i + 1, TableSize - 1)] - Table[i]) * f;
    }

    /// <summary>Second derivatives of a natural cubic spline through the points.</summary>
    private static double[] SplineSecondDerivatives(double[] x, double[] y)
    {
        var n = x.Length;
        var m = new double[n];
        var u = new double[n];

        for (var i = 1; i < n - 1; i++)
        {
            var sig = (x[i] - x[i - 1]) / (x[i + 1] - x[i - 1]);
            var p = sig * m[i - 1] + 2;
            m[i] = (sig - 1) / p;
            u[i] = (y[i + 1] - y[i]) / (x[i + 1] - x[i]) - (y[i] - y[i - 1]) / (x[i] - x[i - 1]);
            u[i] = (6 * u[i] / (x[i + 1] - x[i - 1]) - sig * u[i - 1]) / p;
        }

        for (var k = n - 2; k >= 0; k--)
            m[k] = m[k] * m[k + 1] + u[k];

        return m;
    }

    private static double Spline(double[] x, double[] y, double[] m, int i, double t)
    {
        var h = x[i + 1] - x[i];
        var a = (x[i + 1] - t) / h;
        var b = (t - x[i]) / h;
        return a * y[i] + b * y[i + 1] + ((a * a * a - a) * m[i] + (b * b * b - b) * m[i + 1]) * h * h / 6;
    }
}

/// <summary>
/// ProfileGainTableMap (DNG 1.6, version 2 in 1.7): a coarse grid of 1D gain tables, the maker's
/// local tone mapping. For a pixel, the four tables around its position are interpolated
/// bilinearly (outside the grid the edge tables are replicated), and looked up linearly at
/// <c>input × MapPointsN</c>, where the input is <c>clamp(weights · (R, G, B, min, max), 0, 1) ^ gamma</c>
/// of the linear ProPhoto color after the baseline exposure. The gain multiplies R, G and B.
/// </summary>
public sealed class DngGainTableMap
{
    /// <summary>Apple uses 6 x 8 tables of 257 points.</summary>
    private const int MaxTables = 64 * 64;
    private const int MaxGains = 1 << 20;

    public required int PointsV { get; init; }
    public required int PointsH { get; init; }
    public required int PointsN { get; init; }
    public required double SpacingV { get; init; }
    public required double SpacingH { get; init; }
    public required double OriginV { get; init; }
    public required double OriginH { get; init; }
    public required float[] Weights { get; init; }
    public required float Gamma { get; init; }

    /// <summary><c>PointsV x PointsH x PointsN</c>, row-major, table points innermost.</summary>
    public required float[] Gains { get; init; }

    /// <summary>
    /// The map from the tag's bytes, in the file's byte order and else big-endian (a few writers
    /// followed the opcode lists' convention). Null when malformed: wrong size, absurd dimensions,
    /// non-finite or negative gains, or gamma outside 0.25 to 4.
    /// </summary>
    public static DngGainTableMap? Parse(byte[] bytes, bool littleEndian, bool version2) => ParseIn(bytes, littleEndian, version2) ?? (littleEndian ? ParseIn(bytes, false, version2) : null);

    private static DngGainTableMap? ParseIn(byte[] b, bool little, bool version2)
    {
        if (b.Length < 64)
            return null;

        uint U32(int o) => little ? BinaryPrimitives.ReadUInt32LittleEndian(b.AsSpan(o)) : BinaryPrimitives.ReadUInt32BigEndian(b.AsSpan(o));
        float F32(int o) => BitConverter.UInt32BitsToSingle(U32(o));
        double F64(int o) => BitConverter.Int64BitsToDouble(little ? BinaryPrimitives.ReadInt64LittleEndian(b.AsSpan(o)) : BinaryPrimitives.ReadInt64BigEndian(b.AsSpan(o)));
        ushort U16(int o) => little ? BinaryPrimitives.ReadUInt16LittleEndian(b.AsSpan(o)) : BinaryPrimitives.ReadUInt16BigEndian(b.AsSpan(o));

        var pointsV = U32(0);
        var pointsH = U32(4);
        double spacingV = F64(8), spacingH = F64(16), originV = F64(24), originH = F64(32);
        var pointsN = U32(40);
        var weights = new float[5];
        for (var i = 0; i < 5; i++)
            weights[i] = F32(44 + 4 * i);

        if (pointsV == 0 || pointsH == 0 || pointsN == 0 || (ulong)pointsV * pointsH > MaxTables || (ulong)pointsV * pointsH * pointsN > MaxGains)
            return null;

        static bool SpacingOk(double s, uint points) => double.IsFinite(s) && (s > 0 || points == 1);
        if (!SpacingOk(spacingV, pointsV) || !SpacingOk(spacingH, pointsH) || !double.IsFinite(originV) || !double.IsFinite(originH) || weights.Any(w => !float.IsFinite(w)))
            return null;

        int header;
        uint dataType;
        float gamma, gainMin, gainMax;

        if (version2)
        {
            if (b.Length < 80)
                return null;

            header = 80;
            dataType = U32(64);
            gamma = F32(68);
            gainMin = F32(72);
            gainMax = F32(76);
        }
        else
        {
            header = 64;
            dataType = 3;
            gamma = 1f;
            gainMin = 0f;
            gainMax = 1f;
        }

        if (!(gamma >= 0.25f && gamma <= 4f) || !float.IsFinite(gainMin) || !float.IsFinite(gainMax))
            return null;

        var width = dataType switch { 0 => 1, 1 or 2 => 2, 3 => 4, _ => 0 };
        var count = (int)(pointsV * pointsH * pointsN);
        if (width == 0 || b.Length != header + (long)count * width)
            return null;

        var gains = new float[count];
        for (var i = 0; i < count; i++)
        {
            var o = header + i * width;
            gains[i] = dataType switch
            {
                0 => gainMin + b[o] / 255f * (gainMax - gainMin),
                1 => gainMin + U16(o) / 65535f * (gainMax - gainMin),
                2 => (float)BitConverter.UInt16BitsToHalf(U16(o)),
                _ => F32(o)
            };

            if (!float.IsFinite(gains[i]) || gains[i] < 0)
                return null;
        }

        return new DngGainTableMap
        {
            PointsV = (int)pointsV, PointsH = (int)pointsH, PointsN = (int)pointsN,
            SpacingV = spacingV, SpacingH = spacingH, OriginV = originV, OriginH = originH,
            Weights = weights, Gamma = gamma, Gains = gains
        };
    }

    /// <summary>
    /// The gain for a linear ProPhoto colour after the baseline exposure, at (<paramref name="x"/>,
    /// <paramref name="y"/>) relative to the image area (0 to 1 across its width and height).
    /// </summary>
    public float Gain(float r, float g, float b, double x, double y)
    {
        var down = Cell(y, OriginV, SpacingV, PointsV);
        var across = Cell(x, OriginH, SpacingH, PointsH);

        var n = PointsN;
        var last = n - 1;
        var index = MathF.Min(Input(r, g, b) * n, last);
        var i0 = Math.Min((int)index, last);
        var i1 = Math.Min(i0 + 1, last);
        var t = index - i0;

        float At(int v, int h)
        {
            var table = (v * PointsH + h) * n;
            return Gains[table + i0] + (Gains[table + i1] - Gains[table + i0]) * t;
        }

        var top = At(down.First, across.First) + (At(down.First, across.Second) - At(down.First, across.First)) * across.Weight;
        var bottom = At(down.Second, across.First) + (At(down.Second, across.Second) - At(down.Second, across.First)) * across.Weight;
        var gain = top + (bottom - top) * down.Weight;

        return float.IsFinite(gain) ? MathF.Max(gain, 0f) : 1f;
    }

    private float Input(float r, float g, float b)
    {
        var lo = MathF.Min(r, MathF.Min(g, b));
        var hi = MathF.Max(r, MathF.Max(g, b));
        var x = Weights[0] * r + Weights[1] * g + Weights[2] * b + Weights[3] * lo + Weights[4] * hi;
        x = float.IsNaN(x) ? 0f : Math.Clamp(x, 0f, 1f);
        return Gamma == 1f ? x : MathF.Pow(x, Gamma);
    }

    /// <summary>The two tables either side of a position along one axis, and the weight of the second.</summary>
    private readonly record struct GridCell(int First, int Second, float Weight);

    private static GridCell Cell(double position, double origin, double spacing, int points)
    {
        if (points <= 1 || !(spacing > 0))
            return new GridCell(0, 0, 0f);

        var f = (position - origin) / spacing;
        f = double.IsNaN(f) ? 0 : Math.Clamp(f, 0, points - 1);
        var first = Math.Min((int)f, points - 1);
        return new GridCell(first, Math.Min(first + 1, points - 1), (float)(f - first));
    }
}
