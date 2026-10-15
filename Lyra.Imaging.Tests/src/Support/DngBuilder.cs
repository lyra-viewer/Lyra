namespace Lyra.Imaging.Tests.Support;

/// <summary>
/// Writes small uncompressed Bayer DNGs that LibRaw develops like a camera's: a 16-bit RGGB
/// mosaic in its own sub-IFD, the DNG color tags, and optionally an 8-bit RGB preview as IFD0.
/// </summary>
internal static class DngBuilder
{
    /// <param name="Level">Every photosite's value, as a fraction of <paramref name="WhiteLevel"/>.</param>
    /// <param name="Orientation">The TIFF orientation tag; 1 is upright.</param>
    /// <param name="PreviewWidth">0 for no preview.</param>
    /// <param name="MosaicMissing">Points the raw strip past the end of the file, so only the preview can be shown.</param>
    /// <param name="ToneCurve">ProfileToneCurve as (x, y) pairs.</param>
    /// <param name="GainTableMap">A ProfileGainTableMap payload, little-endian like the file; see <see cref="GainTableMap"/>.</param>
    /// <param name="Exif">Entries of an EXIF IFD that IFD0 points at; null for none.</param>
    internal sealed record Spec(
        int Width = 64,
        int Height = 48,
        double Level = 0.25,
        ushort WhiteLevel = 4095,
        ushort Orientation = 1,
        int PreviewWidth = 0,
        int PreviewHeight = 0,
        bool MosaicMissing = false,
        float[]? ToneCurve = null,
        byte[]? GainTableMap = null,
        List<TiffWriter.Entry>? Exif = null
    );

    /// <summary>A little-endian version-1 ProfileGainTableMap: origin 0, spacing 1, every table constant.</summary>
    public static byte[] GainTableMap(int pointsV, int pointsH, float[] tableGains, float[] weights)
    {
        const int pointsN = 2;
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);

        writer.Write((uint)pointsV);
        writer.Write((uint)pointsH);
        writer.Write(1.0); // spacing V
        writer.Write(1.0); // spacing H
        writer.Write(0.0); // origin V
        writer.Write(0.0); // origin H
        writer.Write((uint)pointsN);
        foreach (var weight in weights)
            writer.Write(weight);

        foreach (var gain in tableGains)
            for (var i = 0; i < pointsN; i++)
                writer.Write(gain);

        return stream.ToArray();
    }

    public static byte[] Build(Spec spec)
    {
        var tiff = new TiffWriter();

        var mosaic = Mosaic(spec);
        var raw = new List<TiffWriter.Entry>
        {
            TiffWriter.Long(254, 0),                       // NewSubFileType: main image
            TiffWriter.Long(256, (uint)spec.Width),
            TiffWriter.Long(257, (uint)spec.Height),
            TiffWriter.Shorts(258, 16),                    // BitsPerSample
            TiffWriter.Shorts(259, 1),                     // Compression: none
            TiffWriter.Shorts(262, 32803),                 // Photometric: CFA
            TiffWriter.Shorts(277, 1),                     // SamplesPerPixel
            TiffWriter.Long(278, (uint)spec.Height),       // RowsPerStrip
            TiffWriter.Shorts(284, 1),                     // PlanarConfiguration
            TiffWriter.Shorts(33421, 2, 2),                // CFARepeatPatternDim
            TiffWriter.Bytes(33422, 0, 1, 1, 2),           // CFAPattern: RGGB
            TiffWriter.Long(50714, 0),                     // BlackLevel
            TiffWriter.Long(50717, spec.WhiteLevel)        // WhiteLevel
        };

        // StripOffsets / StripByteCounts
        if (spec.MosaicMissing)
            raw.AddRange([TiffWriter.Long(273, 0x7FFF0000), TiffWriter.Long(279, (uint)mosaic.Length)]);
        else
            raw.Add(TiffWriter.Data(273, mosaic));

        var identity = new[] { 1, 0, 0, 0, 1, 0, 0, 0, 1 };
        var main = new List<TiffWriter.Entry>
        {
            TiffWriter.Ascii(271, "Lyra"),                 // Make
            TiffWriter.Ascii(272, "Synthetic"),            // Model
            TiffWriter.Shorts(274, spec.Orientation),
            TiffWriter.Bytes(50706, 1, 4, 0, 0),           // DNGVersion
            TiffWriter.Bytes(50707, 1, 1, 0, 0),           // DNGBackwardVersion
            TiffWriter.Ascii(50708, "Lyra Synthetic"),     // UniqueCameraModel
            TiffWriter.SRationals(50721, identity),        // ColorMatrix1: camera = XYZ
            TiffWriter.Rationals(50728, 1, 1, 1),          // AsShotNeutral
            TiffWriter.Shorts(50778, 21)                   // CalibrationIlluminant1: D65
        };

        if (spec.ToneCurve is { } curve)
            main.Add(TiffWriter.Floats(50940, curve));     // ProfileToneCurve

        if (spec.GainTableMap is { } map)
            main.Add(TiffWriter.Undefined(52525, map));    // ProfileGainTableMap

        if (spec.Exif is { } exif)
            main.Add(TiffWriter.SubIfd(34665, exif));      // ExifIFD

        if (spec.PreviewWidth <= 0)
        {
            tiff.AddIfd([.. main, .. raw]);
            return tiff.ToArray();
        }

        // DNG's usual layout: the preview is IFD0 and the raw data hangs off it as a sub-IFD.
        var preview = new byte[spec.PreviewWidth * spec.PreviewHeight * 3];
        for (var i = 0; i < preview.Length; i += 3)
        {
            preview[i] = 200;
            preview[i + 1] = 100;
            preview[i + 2] = 50;
        }

        tiff.AddIfd([
            .. main,
            TiffWriter.Long(254, 1),                       // NewSubFileType: reduced resolution
            TiffWriter.Long(256, (uint)spec.PreviewWidth),
            TiffWriter.Long(257, (uint)spec.PreviewHeight),
            TiffWriter.Shorts(258, 8, 8, 8),
            TiffWriter.Shorts(259, 1),
            TiffWriter.Shorts(262, 2),                     // Photometric: RGB
            TiffWriter.Data(273, preview),
            TiffWriter.Shorts(277, 3),
            TiffWriter.Long(278, (uint)spec.PreviewHeight),
            TiffWriter.Shorts(284, 1),
            TiffWriter.SubIfd(330, raw)
        ]);

        return tiff.ToArray();
    }

    private static byte[] Mosaic(Spec spec)
    {
        var value = (ushort)Math.Round(spec.Level * spec.WhiteLevel);
        var bytes = new byte[spec.Width * spec.Height * 2];

        for (var i = 0; i < bytes.Length; i += 2)
        {
            bytes[i] = (byte)value;
            bytes[i + 1] = (byte)(value >> 8);
        }

        return bytes;
    }
}

/// <summary>A little-endian classic TIFF writer: directories with their values and data laid out after them.</summary>
internal sealed class TiffWriter
{
    /// <summary>
    /// A directory entry. <see cref="Payload"/> is the value bytes; <see cref="Strip"/> marks
    /// image data whose tag gets the offset and whose next tag (279) its byte count.
    /// </summary>
    internal sealed record Entry(ushort Tag, ushort Type, uint Count, byte[] Payload, bool Strip = false, List<Entry>? Child = null);

    public static Entry Long(ushort tag, uint value) => new(tag, 4, 1, BitConverter.GetBytes(value));

    public static Entry Shorts(ushort tag, params ushort[] values) =>
        new(tag, 3, (uint)values.Length, [.. values.SelectMany(BitConverter.GetBytes)]);

    public static Entry Bytes(ushort tag, params byte[] values) => new(tag, 1, (uint)values.Length, values);

    public static Entry Ascii(ushort tag, string text) => new(tag, 2, (uint)text.Length + 1, [.. System.Text.Encoding.ASCII.GetBytes(text), 0]);

    public static Entry Rationals(ushort tag, params uint[] numerators) =>
        new(tag, 5, (uint)numerators.Length, [.. numerators.SelectMany(n => BitConverter.GetBytes(n).Concat(BitConverter.GetBytes(1u)))]);

    public static Entry Rational(ushort tag, uint numerator, uint denominator) =>
        new(tag, 5, 1, [.. BitConverter.GetBytes(numerator), .. BitConverter.GetBytes(denominator)]);

    public static Entry SRationals(ushort tag, int[] numerators) =>
        new(tag, 10, (uint)numerators.Length, [.. numerators.SelectMany(n => BitConverter.GetBytes(n).Concat(BitConverter.GetBytes(1)))]);

    public static Entry Data(ushort tag, byte[] data) => new(tag, 4, 1, data, Strip: true);

    public static Entry Floats(ushort tag, float[] values) => new(tag, 11, (uint)values.Length, [.. values.SelectMany(BitConverter.GetBytes)]);

    public static Entry Undefined(ushort tag, byte[] bytes) => new(tag, 7, (uint)bytes.Length, bytes);

    public static Entry SubIfd(ushort tag, List<Entry> child) => new(tag, 4, 1, [], Child: child);

    private readonly List<List<Entry>> _ifds = [];

    public void AddIfd(List<Entry> entries) => _ifds.Add(entries);

    public byte[] ToArray()
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);

        writer.Write("II"u8);
        writer.Write((ushort)42);
        var firstPointer = stream.Position;
        writer.Write(0u);

        var previousNext = firstPointer;
        foreach (var ifd in _ifds)
        {
            var at = WriteIfd(writer, ifd, out var nextAt);
            Patch(writer, previousNext, at);
            previousNext = nextAt;
        }

        return stream.ToArray();
    }

    /// <summary>Writes the directory's out-of-line data first, then the directory; returns where it starts.</summary>
    private static uint WriteIfd(BinaryWriter writer, List<Entry> entries, out long nextAt)
    {
        var expanded = new List<(ushort Tag, ushort Type, uint Count, uint Value, byte[]? Inline)>();

        foreach (var entry in entries)
        {
            if (entry.Child is { } child)
            {
                var childAt = WriteIfd(writer, child, out _);
                expanded.Add((entry.Tag, entry.Type, 1, childAt, null));
                continue;
            }

            if (entry.Strip)
            {
                Align(writer);
                var dataAt = (uint)writer.BaseStream.Position;
                writer.Write(entry.Payload);
                expanded.Add((entry.Tag, 4, 1, dataAt, null));
                expanded.Add(((ushort)(entry.Tag + 6), 4, 1, (uint)entry.Payload.Length, null)); // 273 -> 279
                continue;
            }

            if (entry.Payload.Length <= 4)
            {
                expanded.Add((entry.Tag, entry.Type, entry.Count, 0, entry.Payload));
                continue;
            }

            Align(writer);
            var at = (uint)writer.BaseStream.Position;
            writer.Write(entry.Payload);
            expanded.Add((entry.Tag, entry.Type, entry.Count, at, null));
        }

        expanded.Sort((a, b) => a.Tag.CompareTo(b.Tag));

        Align(writer);
        var ifdAt = (uint)writer.BaseStream.Position;
        writer.Write((ushort)expanded.Count);

        foreach (var (tag, type, count, value, inline) in expanded)
        {
            writer.Write(tag);
            writer.Write(type);
            writer.Write(count);

            if (inline is null)
            {
                writer.Write(value);
                continue;
            }

            var padded = new byte[4];
            inline.CopyTo(padded, 0);
            writer.Write(padded);
        }

        nextAt = writer.BaseStream.Position;
        writer.Write(0u);

        return ifdAt;
    }

    private static void Patch(BinaryWriter writer, long at, uint value)
    {
        var resume = writer.BaseStream.Position;
        writer.BaseStream.Position = at;
        writer.Write(value);
        writer.BaseStream.Position = resume;
    }

    private static void Align(BinaryWriter writer)
    {
        if (writer.BaseStream.Position % 2 != 0)
            writer.Write((byte)0);
    }
}