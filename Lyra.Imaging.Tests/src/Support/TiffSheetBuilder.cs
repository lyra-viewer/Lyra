using System.Buffers.Binary;
using System.IO.Compression;

namespace Lyra.Imaging.Tests.Support;

/// <summary>
/// Writes an 8-bit gray TIFF of any size in a few kilobytes: every strip points at the same
/// Deflate-compressed band, so libtiff decodes the full sheet from one small block.
/// </summary>
internal static class TiffSheetBuilder
{
    private const int RowsPerStrip = 64;

    public static byte[] Gray(int width, int height, byte gray)
    {
        var strips = (height + RowsPerStrip - 1) / RowsPerStrip;
        var band = Deflate(width * RowsPerStrip, gray);

        (ushort Tag, ushort Type, uint Count, uint Value)[] entries =
        [
            (256, 4, 1, (uint)width),     // ImageWidth
            (257, 4, 1, (uint)height),    // ImageLength
            (258, 3, 1, 8),               // BitsPerSample
            (259, 3, 1, 8),               // Compression: Deflate
            (262, 3, 1, 1),               // PhotometricInterpretation: BlackIsZero
            (273, 4, (uint)strips, 0),    // StripOffsets, filled in below
            (277, 3, 1, 1),               // SamplesPerPixel
            (278, 4, 1, RowsPerStrip),    // RowsPerStrip
            (279, 4, (uint)strips, 0)     // StripByteCounts, filled in below
        ];

        const int header = 8;
        var offsetsAt = header + 2 + entries.Length * 12 + 4;
        var countsAt = offsetsAt + strips * 4;
        var bandAt = countsAt + strips * 4;

        entries[5].Value = (uint)offsetsAt;
        entries[8].Value = (uint)countsAt;

        var bytes = new byte[bandAt + band.Length];
        var span = bytes.AsSpan();

        "II"u8.CopyTo(span);
        BinaryPrimitives.WriteUInt16LittleEndian(span[2..], 42);
        BinaryPrimitives.WriteUInt32LittleEndian(span[4..], header);
        BinaryPrimitives.WriteUInt16LittleEndian(span[header..], (ushort)entries.Length);

        for (var i = 0; i < entries.Length; i++)
        {
            var entry = span[(header + 2 + i * 12)..];
            var (tag, type, count, value) = entries[i];

            BinaryPrimitives.WriteUInt16LittleEndian(entry, tag);
            BinaryPrimitives.WriteUInt16LittleEndian(entry[2..], type);
            BinaryPrimitives.WriteUInt32LittleEndian(entry[4..], count);

            if (type == 3)
                BinaryPrimitives.WriteUInt16LittleEndian(entry[8..], (ushort)value);
            else
                BinaryPrimitives.WriteUInt32LittleEndian(entry[8..], value);
        }

        // The last strip is shorter, but a strip may decode to more rows than it holds.
        for (var i = 0; i < strips; i++)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(span[(offsetsAt + i * 4)..], (uint)bandAt);
            BinaryPrimitives.WriteUInt32LittleEndian(span[(countsAt + i * 4)..], (uint)band.Length);
        }

        band.CopyTo(span[bandAt..]);
        return bytes;
    }

    private static byte[] Deflate(int length, byte value)
    {
        using var compressed = new MemoryStream();

        using (var zlib = new ZLibStream(compressed, CompressionLevel.SmallestSize, leaveOpen: true))
        {
            var row = new byte[Math.Min(length, 1 << 16)];
            Array.Fill(row, value);

            for (var written = 0; written < length; written += row.Length)
                zlib.Write(row, 0, Math.Min(row.Length, length - written));
        }

        return compressed.ToArray();
    }
}
