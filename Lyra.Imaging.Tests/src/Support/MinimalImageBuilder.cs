using System.Buffers.Binary;
using System.Text;

namespace Lyra.Imaging.Tests.Support;

/// <summary>
/// Writes the smallest valid file each format allows, for tests that only care what the header
/// says. The pixels are arbitrary; only the header fields are ever asserted on.
/// </summary>
internal static class MinimalImageBuilder
{
    private const int Width = 2;
    private const int Height = 2;

    /// <summary>An uncompressed BMP with a BITMAPINFOHEADER. The caller deletes the file.</summary>
    public static string WriteBmp(ushort bitsPerPixel = 24, uint compression = 0)
    {
        var rowBytes = (Width * bitsPerPixel + 7) / 8;
        var paddedRow = (rowBytes + 3) & ~3; // BMP rows are padded to four bytes
        var pixelBytes = paddedRow * Height;

        const int fileHeader = 14;
        const int infoHeader = 40;

        var bytes = new byte[fileHeader + infoHeader + pixelBytes];
        var span = bytes.AsSpan();

        Encoding.ASCII.GetBytes("BM").CopyTo(span);
        BinaryPrimitives.WriteUInt32LittleEndian(span[2..], (uint)bytes.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(span[10..], fileHeader + infoHeader);

        var info = span[fileHeader..];
        BinaryPrimitives.WriteUInt32LittleEndian(info, infoHeader);
        BinaryPrimitives.WriteInt32LittleEndian(info[4..], Width);
        BinaryPrimitives.WriteInt32LittleEndian(info[8..], Height);
        BinaryPrimitives.WriteUInt16LittleEndian(info[12..], 1);
        BinaryPrimitives.WriteUInt16LittleEndian(info[14..], bitsPerPixel);
        BinaryPrimitives.WriteUInt32LittleEndian(info[16..], compression);
        BinaryPrimitives.WriteUInt32LittleEndian(info[20..], (uint)pixelBytes);

        return Write(bytes, ".bmp");
    }

    /// <summary>An uncompressed true-color TGA. The caller deletes the file.</summary>
    public static string WriteTga(byte pixelDepth = 24)
    {
        const int header = 18;
        var bytes = new byte[header + Width * Height * (pixelDepth / 8)];
        var span = bytes.AsSpan();

        span[2] = 2;
        BinaryPrimitives.WriteUInt16LittleEndian(span[12..], Width);
        BinaryPrimitives.WriteUInt16LittleEndian(span[14..], Height);
        span[16] = pixelDepth;

        return Write(bytes, ".tga");
    }
    
    public static string WriteIco(params (int Size, ushort Bits)[] entries)
    {
        const int directory = 6;
        const int entrySize = 16;
        const int payload = 8;

        var bytes = new byte[directory + (entrySize * entries.Length) + (payload * entries.Length)];
        var span = bytes.AsSpan();

        BinaryPrimitives.WriteUInt16LittleEndian(span[2..], 1); // 1 = icon, 2 = cursor
        BinaryPrimitives.WriteUInt16LittleEndian(span[4..], (ushort)entries.Length);

        var offset = directory + (entrySize * entries.Length);

        for (var i = 0; i < entries.Length; i++)
        {
            var entry = span[(directory + (i * entrySize))..];

            // The dimension fields are one byte each, so 256 - the largest icon Windows defines -
            // is written as zero.
            entry[0] = (byte)(entries[i].Size == 256 ? 0 : entries[i].Size);
            entry[1] = entry[0];

            BinaryPrimitives.WriteUInt16LittleEndian(entry[4..], 1); // colour planes
            BinaryPrimitives.WriteUInt16LittleEndian(entry[6..], entries[i].Bits);
            BinaryPrimitives.WriteUInt32LittleEndian(entry[8..], payload);
            BinaryPrimitives.WriteUInt32LittleEndian(entry[12..], (uint)offset);

            offset += payload;
        }

        return Write(bytes, ".ico");
    }

    /// <summary>
    /// An uncompressed 2x2 8-bit grayscale TIFF, as bytes rather than a file: callers that care
    /// what the file is <em>called</em> need to choose the name themselves.
    /// </summary>
    public static byte[] TiffBytes()
    {
        // SHORT and LONG values of a single element live inside the entry itself; every tag here
        // is one element, so the directory needs no out-of-line values and the pixels can follow
        // it directly.
        (ushort Tag, ushort Type, uint Value)[] entries =
        [
            (256, 3, Width),         // ImageWidth
            (257, 3, Height),        // ImageLength
            (258, 3, 8),             // BitsPerSample
            (259, 3, 1),             // Compression: none
            (262, 3, 1),             // PhotometricInterpretation: BlackIsZero
            (273, 4, 0),             // StripOffsets, filled in below
            (277, 3, 1),             // SamplesPerPixel
            (278, 3, Height),        // RowsPerStrip
            (279, 4, Width * Height) // StripByteCounts
        ];

        const int header = 8;
        var directory = 2 + (entries.Length * 12) + 4;
        var pixelsAt = header + directory;

        entries[5].Value = (uint)pixelsAt;

        var bytes = new byte[pixelsAt + (Width * Height)];
        var span = bytes.AsSpan();

        span[0] = (byte)'I';
        span[1] = (byte)'I';
        BinaryPrimitives.WriteUInt16LittleEndian(span[2..], 42);
        BinaryPrimitives.WriteUInt32LittleEndian(span[4..], header);

        BinaryPrimitives.WriteUInt16LittleEndian(span[header..], (ushort)entries.Length);

        for (var i = 0; i < entries.Length; i++)
        {
            var entry = span[(header + 2 + (i * 12))..];
            var (tag, type, value) = entries[i];

            BinaryPrimitives.WriteUInt16LittleEndian(entry, tag);
            BinaryPrimitives.WriteUInt16LittleEndian(entry[2..], type);
            BinaryPrimitives.WriteUInt32LittleEndian(entry[4..], 1); // one element

            // A SHORT sits in the first half of the value field; a LONG fills it.
            if (type == 3)
                BinaryPrimitives.WriteUInt16LittleEndian(entry[8..], (ushort)value);
            else
                BinaryPrimitives.WriteUInt32LittleEndian(entry[8..], value);
        }

        // Next-directory offset stays zero: this file holds one page.

        for (var i = 0; i < Width * Height; i++)
            bytes[pixelsAt + i] = (byte)(i * 60);

        return bytes;
    }

    private static string Write(byte[] bytes, string extension)
    {
        var path = Path.Combine(Path.GetTempPath(), $"lyra-minimal-{Guid.NewGuid():N}{extension}");
        File.WriteAllBytes(path, bytes);
        return path;
    }
}