using System.Buffers.Binary;
using Lyra.ManagedCodecs.Raster.Dng;
using Lyra.Metadata.Tiff;
using Xunit;

namespace Lyra.ManagedCodecs.Tests.Dng;

public class DngRenderingProfileTests
{
    // --- Tone curve ---------------------------------------------------------------------------

    [Fact]
    public void TheCurve_PassesThroughItsPoints()
    {
        var curve = DngToneCurve.FromPairs([0f, 0f, 0.25f, 0.1f, 0.5f, 0.4f, 0.75f, 0.8f, 1f, 1f])!;

        Assert.Equal(0f, curve.Table[0]);
        Assert.Equal(0.1f, At(curve, 0.25f), 0.003f);
        Assert.Equal(0.4f, At(curve, 0.5f), 0.003f);
        Assert.Equal(0.8f, At(curve, 0.75f), 0.003f);
        Assert.Equal(1f, curve.Table[^1]);
    }

    private static float At(DngToneCurve curve, float value)
    {
        var position = value * (DngToneCurve.TableSize - 1);
        var i = (int)position;
        return curve.Table[i] + (curve.Table[i + 1] - curve.Table[i]) * (position - i);
    }

    [Fact]
    public void TheCurve_StaysWithinTheUnitRange()
    {
        // A steep step would make a spline overshoot.
        var curve = DngToneCurve.FromPairs([0f, 0f, 0.49f, 0f, 0.51f, 1f, 1f, 1f])!;

        Assert.All(curve.Table, value => Assert.InRange(value, 0f, 1f));
    }

    [Theory]
    [InlineData(new[] { 0f, 0f })] // one point
    [InlineData(new[] { 0f, 0f, 0.5f })] // odd count
    [InlineData(new[] { 0f, 0f, 0.5f, 0.5f, 0.4f, 1f })] // inputs not increasing
    [InlineData(new[] { 0f, 0f, 1.5f, 1f })] // input beyond 1
    [InlineData(new[] { 0f, 0f, 1f, float.NaN })] // not a number
    public void APointListThatIsNotACurve_IsRejected(float[] pairs)
    {
        Assert.Null(DngToneCurve.FromPairs(pairs));
    }

    // --- Gain table map ---------------------------------------------------------------------

    [Fact]
    public void TheGain_IsLookedUpAtTheInputTimesN()
    {
        // One table of N = 4: gains 1, 2, 3, 4. Weight 1 on G: a grey of 0.5 reads index 2.
        var map = Parse(Map(1, 1, 4, [0, 1, 0, 0, 0], [1, 2, 3, 4]))!;

        Assert.Equal(3f, map.Gain(0.5f, 0.5f, 0.5f, 0.5, 0.5), 0.0001f);
        Assert.Equal(2.5f, map.Gain(0.375f, 0.375f, 0.375f, 0.5, 0.5), 0.0001f);

        // Beyond the end the last point holds.
        Assert.Equal(4f, map.Gain(1f, 1f, 1f, 0.5, 0.5), 0.0001f);
    }

    [Fact]
    public void Tables_AreInterpolatedAcrossTheImage_AndReplicatedPastItsEdges()
    {
        // Two tables side by side, origin 0 and spacing 1: constant gains 1 (left) and 3 (right).
        var map = Parse(Map(1, 2, 2, [0, 1, 0, 0, 0], [1, 1, 3, 3], spacingH: 1))!;

        Assert.Equal(1f, map.Gain(0.2f, 0.2f, 0.2f, 0.0, 0.5), 0.0001f);
        Assert.Equal(2f, map.Gain(0.2f, 0.2f, 0.2f, 0.5, 0.5), 0.0001f);
        Assert.Equal(3f, map.Gain(0.2f, 0.2f, 0.2f, 1.0, 0.5), 0.0001f);
        Assert.Equal(3f, map.Gain(0.2f, 0.2f, 0.2f, 7.0, 0.5), 0.0001f);
        Assert.Equal(1f, map.Gain(0.2f, 0.2f, 0.2f, -7.0, 0.5), 0.0001f);
    }

    [Fact]
    public void AVersion2Map_ScalesIntegerGainsAndAppliesItsGamma()
    {
        // u8 gains 0 and 255 over GainMin 1 .. GainMax 3, gamma 2.
        var bytes = Map(1, 1, 2, [0, 1, 0, 0, 0], [], version2: (DataType: 0u, Gamma: 2f, Min: 1f, Max: 3f), integerGains: [0, 255]);
        var map = DngGainTableMap.Parse(bytes, littleEndian: false, version2: true)!;

        Assert.Equal([1f, 3f], map.Gains);
        Assert.Equal(2f, map.Gamma);

        // Input 0.5 squared is 0.25, index 0.5 between gains 1 and 3.
        Assert.Equal(2f, map.Gain(0.5f, 0.5f, 0.5f, 0.5, 0.5), 0.0001f);
    }

    [Fact]
    public void ABigEndianMapInALittleEndianFile_IsStillRead()
    {
        Assert.NotNull(DngGainTableMap.Parse(Map(1, 1, 4, [0, 1, 0, 0, 0], [1, 2, 3, 4]), littleEndian: true, version2: false));
    }

    [Fact]
    public void MalformedMaps_AreRejected()
    {
        var good = Map(1, 1, 4, [0, 1, 0, 0, 0], [1, 2, 3, 4]);

        Assert.Null(Parse(good[..^1])); // truncated
        Assert.Null(Parse([.. good, 0])); // trailing byte
        Assert.Null(Parse(Map(1, 1, 4, [0, 1, 0, 0, 0], [1, -2, 3, 4]))); // negative gain
        Assert.Null(Parse(Map(1, 1, 4, [0, 1, 0, 0, 0], [1, float.NaN, 3, 4]))); // not a number
        Assert.Null(Parse(Map(1, 2, 2, [0, 1, 0, 0, 0], [1, 1, 3, 3], spacingH: 0))); // no spacing between tables
        Assert.Null(Parse(Map(100_000, 100_000, 1, [0, 1, 0, 0, 0], []))); // absurd grid
        Assert.Null(DngGainTableMap.Parse(Map(1, 1, 2, [0, 1, 0, 0, 0], [], version2: (0u, 9f, 0f, 1f), integerGains: [0, 1]), false, true)); // gamma
    }

    // --- Reading from a file ----------------------------------------------------------------

    [Fact]
    public void TheProfile_IsFoundInTheFile()
    {
        var tiff = Tiff(
        [
            (DngRenderingProfile.TagProfileToneCurve, 11, 4u, Floats([0f, 0f, 1f, 1f])),
            (DngRenderingProfile.TagProfileGainTableMap, 7, 0u, Map(1, 1, 4, [0, 1, 0, 0, 0], [1, 2, 3, 4]))
        ]);

        using var stream = new MemoryStream(tiff);
        var profile = DngRenderingProfile.Read(stream);

        Assert.NotNull(profile);
        Assert.Equal(2, profile.ToneCurve!.PointCount);
        Assert.Equal(4, profile.GainTableMap!.PointsN);
    }

    [Fact]
    public void AFileWithoutEither_HasNoProfile()
    {
        using var stream = new MemoryStream(Tiff([(271, 2, 5u, "Lyra\0"u8.ToArray())]));

        Assert.Null(DngRenderingProfile.Read(stream));
    }

    [Fact]
    public void ADirectoryChainThatLoops_EndsTheWalk()
    {
        var tiff = Tiff([(271, 2, 5u, "Lyra\0"u8.ToArray())]);

        // Point the directory's "next" back at itself.
        var ifd = (int)BinaryPrimitives.ReadUInt32BigEndian(tiff.AsSpan(4));
        var count = BinaryPrimitives.ReadUInt16BigEndian(tiff.AsSpan(ifd));
        BinaryPrimitives.WriteUInt32BigEndian(tiff.AsSpan(ifd + 2 + count * 12), (uint)ifd);

        using var stream = new MemoryStream(tiff);
        var layout = TiffDirectoryReader.Read(stream);

        Assert.NotNull(layout);
        Assert.Single(layout.Directories);
    }

    [Theory]
    [InlineData(new byte[] { })]
    [InlineData(new byte[] { 0x4D, 0x4D })]
    [InlineData(new byte[] { 0x4D, 0x4D, 0x00, 0x2A, 0xFF, 0xFF, 0xFF, 0xFF })] // directory past the end
    [InlineData(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A })] // a PNG
    public void SomethingThatIsNotATiff_IsNotRead(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes);

        Assert.Null(TiffDirectoryReader.Read(stream));
    }

    // --- Builders -----------------------------------------------------------------------------

    private static DngGainTableMap? Parse(byte[] bytes) => DngGainTableMap.Parse(bytes, littleEndian: false, version2: false);

    /// <summary>A big-endian ProfileGainTableMap payload, version 1 (float gains) unless <paramref name="version2"/> is given.</summary>
    private static byte[] Map(uint v, uint h, uint n, float[] weights, float[] gains, double spacingH = 1, (uint DataType, float Gamma, float Min, float Max)? version2 = null, byte[]? integerGains = null)
    {
        using var s = new MemoryStream();

        void U32(uint value)
        {
            Span<byte> b = stackalloc byte[4];
            BinaryPrimitives.WriteUInt32BigEndian(b, value);
            s.Write(b);
        }

        void F32(float value) => U32(BitConverter.SingleToUInt32Bits(value));

        void F64(double value)
        {
            Span<byte> b = stackalloc byte[8];
            BinaryPrimitives.WriteInt64BigEndian(b, BitConverter.DoubleToInt64Bits(value));
            s.Write(b);
        }

        U32(v);
        U32(h);
        F64(1);        // spacing V
        F64(spacingH); // spacing H
        F64(0);        // origin V
        F64(0);        // origin H
        U32(n);
        foreach (var w in weights)
            F32(w);

        if (version2 is { } header)
        {
            U32(header.DataType);
            F32(header.Gamma);
            F32(header.Min);
            F32(header.Max);
        }

        if (integerGains is not null)
            s.Write(integerGains);
        else
            foreach (var gain in gains)
                F32(gain);

        return s.ToArray();
    }

    private static byte[] Floats(float[] values)
    {
        var bytes = new byte[values.Length * 4];
        for (var i = 0; i < values.Length; i++)
            BinaryPrimitives.WriteSingleBigEndian(bytes.AsSpan(i * 4), values[i]);

        return bytes;
    }

    /// <summary>A big-endian TIFF with one directory holding the given entries, every value stored after it.</summary>
    private static byte[] Tiff((ushort Tag, ushort Type, uint Count, byte[] Value)[] entries)
    {
        using var s = new MemoryStream();
        var w = new BinaryWriter(s);

        void U16(ushort value)
        {
            Span<byte> b = stackalloc byte[2];
            BinaryPrimitives.WriteUInt16BigEndian(b, value);
            s.Write(b);
        }

        void U32(uint value)
        {
            Span<byte> b = stackalloc byte[4];
            BinaryPrimitives.WriteUInt32BigEndian(b, value);
            s.Write(b);
        }

        s.Write("MM"u8);
        U16(42);
        U32(8);

        var sorted = entries.OrderBy(e => e.Tag).ToArray();
        var dataAt = 8 + 2 + sorted.Length * 12 + 4;

        U16((ushort)sorted.Length);
        foreach (var (tag, type, count, value) in sorted)
        {
            U16(tag);
            U16(type);
            U32(count == 0 ? (uint)value.Length : count);

            if (value.Length <= 4)
            {
                var padded = new byte[4];
                value.CopyTo(padded, 0);
                s.Write(padded);
            }
            else
            {
                U32((uint)dataAt);
                dataAt += value.Length;
            }
        }

        U32(0);

        foreach (var (_, _, _, value) in sorted)
            if (value.Length > 4)
                s.Write(value);

        w.Flush();
        return s.ToArray();
    }
}