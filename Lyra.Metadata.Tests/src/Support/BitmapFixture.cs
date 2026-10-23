using System.Buffers.Binary;
using System.Text;

namespace Lyra.Metadata.Tests.Support;

/// <summary>Builds the headers of BMP, ICO, TGA and PSD files, with no pixels anyone could decode.</summary>
internal static class BitmapFixture
{
    // BMP

    /// <summary>A file header and an info header of <paramref name="headerSize"/> bytes; a V5 header can embed or link a profile.</summary>
    public static byte[] Bmp(int headerSize, ushort bits, int compression = 0, byte[]? profile = null, bool linked = false, string magic = "BM")
    {
        var header = new byte[headerSize];
        BinaryPrimitives.WriteInt32LittleEndian(header, headerSize);
        BinaryPrimitives.WriteInt16LittleEndian(header.AsSpan(4), 8);
        BinaryPrimitives.WriteInt16LittleEndian(header.AsSpan(headerSize == 12 ? 6 : 8), 8);
        BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(headerSize == 12 ? 8 : 12), 1);
        BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(headerSize == 12 ? 10 : 14), bits);

        if (headerSize >= 20)
            BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(16), compression);

        if (headerSize == 124 && profile is not null)
        {
            Encoding.ASCII.GetBytes(linked ? "KNIL" : "DEBM").CopyTo(header, 56);
            BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(112), headerSize + 4);
            BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(116), profile.Length);
        }

        byte[] file = [.. Encoding.ASCII.GetBytes(magic), .. new byte[12], .. header];
        return profile is null || headerSize != 124 ? [.. file, 1, 2, 3, 4] : [.. file, 1, 2, 3, 4, .. profile];
    }

    /// <summary>An OS/2 bitmap array: each entry a "BA" header pointing at the next, then a file of its own.</summary>
    public static byte[] BitmapArray(params byte[][] files)
    {
        var result = new List<byte>();
        for (var i = 0; i < files.Length; i++)
        {
            var next = i == files.Length - 1 ? 0 : result.Count + 14 + files[i].Length;
            result.AddRange([(byte)'B', (byte)'A', 0, 0, 0, 0, .. U32(next), 0, 0, 0, 0, .. files[i]]);
        }

        return [.. result];
    }

    // ICO

    internal readonly record struct IconEntry(byte Width, byte Height, ushort Bits);

    public static byte[] Ico(params IconEntry[] entries)
    {
        return [0, 0, 1, 0, .. U16(entries.Length), .. entries.SelectMany(entry => (byte[])[entry.Width, entry.Height, 0, 0, .. U16(1), .. U16(entry.Bits), .. U32(40), .. U32(0)])];
    }

    // TGA

    public static byte[] Tga(byte dataType = 2, byte depth = 24, byte colormapType = 0, byte colormapDepth = 24, short width = 8, short height = 8, byte flags = 0)
    {
        var header = new byte[18];
        header[1] = colormapType;
        header[2] = dataType;
        BinaryPrimitives.WriteInt16LittleEndian(header.AsSpan(5), (short)(colormapType == 1 ? 2 : 0));
        header[7] = colormapType == 1 ? colormapDepth : (byte)0;
        BinaryPrimitives.WriteInt16LittleEndian(header.AsSpan(12), width);
        BinaryPrimitives.WriteInt16LittleEndian(header.AsSpan(14), height);
        header[16] = depth;
        header[17] = flags;
        return [.. header, .. new byte[32]];
    }

    // PSD

    /// <summary>A PSD header, an empty color mode section and the resource section given; nothing after.</summary>
    public static byte[] Psd(byte[] resources)
    {
        return [.. "8BPS"u8, 0, 1, 0, 0, 0, 0, 0, 0, 0, 3, .. U32Be(8), .. U32Be(8), 0, 8, 0, 3, .. U32Be(0), .. U32Be((uint)resources.Length), .. resources, .. U32Be(0)];
    }

    private static byte[] U16(int value)
    {
        var bytes = new byte[2];
        BinaryPrimitives.WriteUInt16LittleEndian(bytes, (ushort)value);
        return bytes;
    }

    private static byte[] U32(int value)
    {
        var bytes = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(bytes, value);
        return bytes;
    }

    private static byte[] U32Be(uint value)
    {
        var bytes = new byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(bytes, value);
        return bytes;
    }
}