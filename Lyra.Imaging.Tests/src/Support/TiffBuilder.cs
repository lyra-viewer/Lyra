using System.IO.Compression;

namespace Lyra.Imaging.Tests.Support;

/// <summary>
/// Writes small classic TIFFs of any shape the decoder routes on: page count, size, samples, bit
/// depth and sample format. As with <see cref="TiffSheetBuilder"/>, every strip of a page points at
/// the same Deflate-compressed band, so even a sheet of hundreds of megapixels is a few kilobytes.
/// </summary>
internal static class TiffBuilder
{
    /// <param name="Photometric">1 = BlackIsZero, 2 = RGB.</param>
    /// <param name="SampleFormat">1 = unsigned integer, 3 = IEEE float.</param>
    /// <param name="Fill">Every byte of the page. 0x3F as 32-bit float is about 0.75.</param>
    internal sealed record Page(
        int Width,
        int Height,
        ushort Samples = 1,
        ushort Bits = 8,
        ushort SampleFormat = 1,
        ushort Photometric = 1,
        byte Fill = 0x80,
        int RowsPerStrip = 64,
        byte[]? Icc = null,
        ushort Orientation = 1
    );

    public static Page Gray(int width, int height, ushort bits = 8) => new(width, height, Bits: bits);
    
    public static Page GrayWithIcc(int width, int height) => new(width, height, Icc: TempFile.Pattern(256));

    public static Page GrayOriented(int width, int height, ushort orientation) => new(width, height, Orientation: orientation);

    public static Page Rgb(int width, int height) => new(width, height, Samples: 3, Photometric: 2);

    public static Page FloatGray(int width, int height) => new(width, height, Bits: 32, SampleFormat: 3, Fill: 0x3F);

    public static byte[] Build(params Page[] pages)
    {
        using var file = new MemoryStream();
        using var writer = new BinaryWriter(file);

        writer.Write("II"u8);
        writer.Write((ushort)42);

        var nextIfdPointerAt = file.Position;
        writer.Write(0u);

        foreach (var page in pages)
        {
            var (ifdAt, nextAt) = WritePage(writer, page);

            var resumeAt = file.Position;
            file.Position = nextIfdPointerAt;
            writer.Write(ifdAt);
            file.Position = resumeAt;

            nextIfdPointerAt = nextAt;
        }

        return file.ToArray();
    }

    /// <summary>Writes a page's data, then its directory; returns where the directory starts and where its next-directory pointer is.</summary>
    private static (uint IfdAt, uint NextAt) WritePage(BinaryWriter writer, Page page)
    {
        var rowBytes = (page.Width * page.Bits * page.Samples + 7) / 8;
        var strips = (page.Height + page.RowsPerStrip - 1) / page.RowsPerStrip;

        var bandAt = Here(writer);
        var band = Deflate(rowBytes * page.RowsPerStrip, page.Fill);
        writer.Write(band);

        var offsets = WriteLongs(writer, Enumerable.Repeat(bandAt, strips));
        var counts = WriteLongs(writer, Enumerable.Repeat((uint)band.Length, strips));
        var bits = WriteShorts(writer, Enumerable.Repeat(page.Bits, page.Samples));
        var formats = WriteShorts(writer, Enumerable.Repeat(page.SampleFormat, page.Samples));

        Align(writer);
        var iccAt = Here(writer);
        if (page.Icc is { } icc)
            writer.Write(icc);

        Align(writer);
        var ifdAt = Here(writer);

        // Tags must be in ascending order.
        List<(ushort Tag, ushort Type, uint Count, uint Value)> entries =
        [
            (256, 4, 1, (uint)page.Width),               // ImageWidth
            (257, 4, 1, (uint)page.Height),              // ImageLength
            (258, 3, page.Samples, bits),                // BitsPerSample
            (259, 3, 1, 8),                              // Compression: Deflate
            (262, 3, 1, page.Photometric),               // PhotometricInterpretation
            (273, 4, (uint)strips, offsets),             // StripOffsets
            (277, 3, 1, page.Samples),                   // SamplesPerPixel
            (278, 4, 1, (uint)page.RowsPerStrip),        // RowsPerStrip
            (279, 4, (uint)strips, counts),              // StripByteCounts
            (284, 3, 1, 1),                              // PlanarConfiguration: contiguous
            (339, 3, page.Samples, formats)              // SampleFormat
        ];

        if (page.Icc is { } profile)
            entries.Add((34675, 7, (uint)profile.Length, iccAt)); // ICCProfile, UNDEFINED bytes

        if (page.Orientation != 1)
            entries.Add((274, 3, 1, page.Orientation));           // Orientation

        // Tags must be in ascending order.
        entries.Sort((a, b) => a.Tag.CompareTo(b.Tag));

        writer.Write((ushort)entries.Count);

        foreach (var (tag, type, count, value) in entries)
        {
            writer.Write(tag);
            writer.Write(type);
            writer.Write(count);

            if (type == 3 && count == 1)
            {
                writer.Write((ushort)value);
                writer.Write((ushort)0);
            }
            else
            {
                writer.Write(value);
            }
        }

        var nextAt = Here(writer);
        writer.Write(0u); // next directory, patched by the caller when there is one

        return (ifdAt, nextAt);
    }
    
    private static uint WriteLongs(BinaryWriter writer, IEnumerable<uint> values)
    {
        var array = values.ToArray();
        if (array.Length == 1)
            return array[0];

        Align(writer);
        var at = Here(writer);

        foreach (var value in array)
            writer.Write(value);

        return at;
    }

    private static uint WriteShorts(BinaryWriter writer, IEnumerable<ushort> values)
    {
        var array = values.ToArray();

        if (array.Length == 1)
            return array[0];

        if (array.Length == 2)
            return (uint)(array[0] | array[1] << 16);

        Align(writer);
        var at = Here(writer);

        foreach (var value in array)
            writer.Write(value);

        return at;
    }

    private static uint Here(BinaryWriter writer) => (uint)writer.BaseStream.Position;

    private static void Align(BinaryWriter writer)
    {
        if (writer.BaseStream.Position % 2 != 0)
            writer.Write((byte)0);
    }

    private static byte[] Deflate(int length, byte value)
    {
        using var compressed = new MemoryStream();

        using (var zlib = new ZLibStream(compressed, CompressionLevel.SmallestSize, leaveOpen: true))
        {
            var chunk = new byte[Math.Min(length, 1 << 16)];
            Array.Fill(chunk, value);

            for (var written = 0; written < length; written += chunk.Length)
                zlib.Write(chunk, 0, Math.Min(chunk.Length, length - written));
        }

        return compressed.ToArray();
    }
}
