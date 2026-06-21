using System.Buffers.Binary;
using System.Text;

namespace Lyra.ManagedCodecs.Tests.Texture;

/// <summary>Builds minimal in-memory .ktx / .ktx2 files (header + payload) for reader tests.</summary>
internal static class KtxTestFile
{
    private static ReadOnlySpan<byte> Ktx1Id => [0xAB, 0x4B, 0x54, 0x58, 0x20, 0x31, 0x31, 0xBB, 0x0D, 0x0A, 0x1A, 0x0A];
    private static ReadOnlySpan<byte> Ktx2Id => [0xAB, 0x4B, 0x54, 0x58, 0x20, 0x32, 0x30, 0xBB, 0x0D, 0x0A, 0x1A, 0x0A];

    // ------------------------------------------------------------------
    //  KTX 1.x
    // ------------------------------------------------------------------

    /// <summary>A 2D KTX1 with one mip per entry in <paramref name="mips"/> (largest first).</summary>
    public static byte[] Ktx1(uint glInternalFormat, int width, int height, byte[][] mips, string? orientation = null)
        => Ktx1Build(glInternalFormat, width, height, depth: 0, arrayElements: 0, faceCount: 1, mips, orientation);

    /// <summary>A single-mip non-array cubemap: <paramref name="face"/> is the per-face payload, repeated 6x.</summary>
    public static byte[] Ktx1Cubemap(uint glInternalFormat, int width, int height, byte[] face)
        => Ktx1Build(glInternalFormat, width, height, depth: 0, arrayElements: 0, faceCount: 6, [face], orientation: null);

    /// <summary>A single-mip 3D (volume) texture; <paramref name="volume"/> is the whole level payload.</summary>
    public static byte[] Ktx1Volume(uint glInternalFormat, int width, int height, int depth, byte[] volume)
        => Ktx1Build(glInternalFormat, width, height, depth, arrayElements: 0, faceCount: 1, [volume], orientation: null);

    /// <summary>
    /// A 2D big-endian KTX1: the endianness marker and every header field are written big-endian, and
    /// each mip payload (supplied little-endian and row-padded, like <see cref="Ktx1"/>) is byte-swapped
    /// in <paramref name="glTypeSize"/> chunks to mimic a big-endian producer. The padding bytes are
    /// zero, so swapping the whole buffer leaves them untouched.
    /// </summary>
    public static byte[] Ktx1BigEndian(uint glInternalFormat, uint glType, uint glTypeSize, int width, int height, byte[][] mips)
    {
        using var ms = new MemoryStream();
        ms.Write(Ktx1Id);
        Write32Be(ms, 0x04030201);   // endianness marker, big-endian byte order -> 04 03 02 01
        Write32Be(ms, glType);
        Write32Be(ms, glTypeSize);
        Write32Be(ms, 0);            // glFormat
        Write32Be(ms, glInternalFormat);
        Write32Be(ms, 0);            // glBaseInternalFormat
        Write32Be(ms, (uint)width);
        Write32Be(ms, (uint)height);
        Write32Be(ms, 0);            // pixelDepth
        Write32Be(ms, 0);            // numberOfArrayElements
        Write32Be(ms, 1);            // numberOfFaces
        Write32Be(ms, (uint)mips.Length);
        Write32Be(ms, 0);            // bytesOfKeyValueData

        foreach (var mip in mips)
        {
            var be = (byte[])mip.Clone();
            SwapChunks(be, (int)glTypeSize);
            Write32Be(ms, (uint)be.Length);
            ms.Write(be);
            Pad(ms, be.Length); // mipPadding
        }

        return ms.ToArray();
    }

    private static void SwapChunks(byte[] data, int width)
    {
        if (width <= 1)
        {
            return;
        }

        for (var i = 0; i + width <= data.Length; i += width)
        {
            Array.Reverse(data, i, width);
        }
    }

    private static void Write32Be(MemoryStream ms, uint value)
    {
        Span<byte> b = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(b, value);
        ms.Write(b);
    }

    private static byte[] Ktx1Build(
        uint glInternalFormat, int width, int height, int depth, int arrayElements, int faceCount,
        byte[][] mips, string? orientation)
    {
        var kv = orientation is null ? [] : BuildKeyValue("KTXorientation", orientation);

        using var ms = new MemoryStream();
        ms.Write(Ktx1Id);
        Write32(ms, 0x04030201);     // endianness
        Write32(ms, 0);              // glType (compressed/unspecified)
        Write32(ms, 1);              // glTypeSize
        Write32(ms, 0);              // glFormat
        Write32(ms, glInternalFormat);
        Write32(ms, 0);              // glBaseInternalFormat
        Write32(ms, (uint)width);
        Write32(ms, (uint)height);
        Write32(ms, (uint)depth);
        Write32(ms, (uint)arrayElements);
        Write32(ms, (uint)faceCount);
        Write32(ms, (uint)mips.Length);
        Write32(ms, (uint)kv.Length);
        ms.Write(kv);

        foreach (var mip in mips)
        {
            // For a non-array cubemap imageSize is the per-face size; for a 2D texture it is the whole
            // level. Either way our test payload is one surface, so imageSize is just its length.
            var imageSize = mip.Length;
            Write32(ms, (uint)imageSize);

            if (faceCount == 6)
            {
                for (var f = 0; f < 6; f++)
                {
                    ms.Write(mip);
                    Pad(ms, mip.Length); // cubePadding
                }
            }
            else
            {
                ms.Write(mip);
            }

            Pad(ms, imageSize); // mipPadding
        }

        return ms.ToArray();
    }

    // ------------------------------------------------------------------
    //  KTX 2.0
    // ------------------------------------------------------------------

    /// <summary>A 2D KTX2 with one mip per entry in <paramref name="mips"/> (level 0 = largest first).</summary>
    public static byte[] Ktx2(uint vkFormat, int width, int height, byte[][] mips, uint supercompression = 0, string? orientation = null)
    {
        var kv = orientation is null ? [] : BuildKeyValue("KTXorientation", orientation);

        var levelIndexSize = mips.Length * 24;
        var kvdByteOffset = 80 + levelIndexSize;
        var dataStart = kvdByteOffset + kv.Length;

        // Compute each level's offset; the level index lists level 0 first, data packed in that order.
        var offsets = new long[mips.Length];
        var cursor = (long)dataStart;
        for (var i = 0; i < mips.Length; i++)
        {
            offsets[i] = cursor;
            cursor += mips[i].Length;
        }

        using var ms = new MemoryStream();
        ms.Write(Ktx2Id);
        Write32(ms, vkFormat);
        Write32(ms, 1);                 // typeSize
        Write32(ms, (uint)width);
        Write32(ms, (uint)height);
        Write32(ms, 0);                 // pixelDepth
        Write32(ms, 0);                 // layerCount
        Write32(ms, 1);                 // faceCount
        Write32(ms, (uint)mips.Length); // levelCount
        Write32(ms, supercompression);
        Write32(ms, 0);                 // dfdByteOffset
        Write32(ms, 0);                 // dfdByteLength
        Write32(ms, (uint)(kv.Length == 0 ? 0 : kvdByteOffset));
        Write32(ms, (uint)kv.Length);
        Write64(ms, 0);                 // sgdByteOffset
        Write64(ms, 0);                 // sgdByteLength

        for (var i = 0; i < mips.Length; i++)
        {
            Write64(ms, (ulong)offsets[i]);          // byteOffset
            Write64(ms, (ulong)mips[i].Length);      // byteLength
            Write64(ms, (ulong)mips[i].Length);      // uncompressedByteLength
        }

        ms.Write(kv);
        foreach (var mip in mips)
        {
            ms.Write(mip);
        }

        return ms.ToArray();
    }

    /// <summary>A single-level KTX2 whose level data is supercompressed (caller supplies the compressed
    /// bytes and the original uncompressed length the level index should advertise).</summary>
    public static byte[] Ktx2Supercompressed(uint vkFormat, int width, int height, uint scheme, byte[] compressed, int uncompressedLength)
    {
        const int levelIndexSize = 24;
        var dataStart = 80 + levelIndexSize;

        using var ms = new MemoryStream();
        ms.Write(Ktx2Id);
        Write32(ms, vkFormat);
        Write32(ms, 1);
        Write32(ms, (uint)width);
        Write32(ms, (uint)height);
        Write32(ms, 0);
        Write32(ms, 0);
        Write32(ms, 1);  // faceCount
        Write32(ms, 1);  // levelCount
        Write32(ms, scheme);
        Write32(ms, 0);
        Write32(ms, 0);
        Write32(ms, 0);  // kvdByteOffset
        Write32(ms, 0);  // kvdByteLength
        Write64(ms, 0);
        Write64(ms, 0);

        Write64(ms, (ulong)dataStart);            // byteOffset
        Write64(ms, (ulong)compressed.Length);    // byteLength (compressed)
        Write64(ms, (ulong)uncompressedLength);   // uncompressedByteLength

        ms.Write(compressed);
        return ms.ToArray();
    }

    // ------------------------------------------------------------------
    //  Payload helpers
    // ------------------------------------------------------------------

    /// <summary>A solid-red BC1 4x4 block (color0 = red565, all indices 0).</summary>
    public static byte[] RedBc1Block() => [0x00, 0xF8, 0x00, 0xF8, 0x00, 0x00, 0x00, 0x00];

    /// <summary>A width*height RGBA8 surface filled with one color.</summary>
    public static byte[] Rgba8(int width, int height, byte r, byte g, byte b, byte a)
    {
        var buf = new byte[width * height * 4];
        for (var i = 0; i < width * height; i++)
        {
            buf[i * 4 + 0] = r;
            buf[i * 4 + 1] = g;
            buf[i * 4 + 2] = b;
            buf[i * 4 + 3] = a;
        }

        return buf;
    }

    private static byte[] BuildKeyValue(string key, string value)
    {
        var keyBytes = Encoding.UTF8.GetBytes(key);
        var valueBytes = Encoding.UTF8.GetBytes(value);
        var size = keyBytes.Length + 1 + valueBytes.Length + 1; // key \0 value \0

        using var ms = new MemoryStream();
        Write32(ms, (uint)size);
        ms.Write(keyBytes);
        ms.WriteByte(0);
        ms.Write(valueBytes);
        ms.WriteByte(0);
        Pad(ms, size); // value padding to 4-byte boundary

        return ms.ToArray();
    }

    private static void Pad(MemoryStream ms, long size)
    {
        var pad = (int)((4 - (size & 3)) & 3);
        for (var i = 0; i < pad; i++)
        {
            ms.WriteByte(0);
        }
    }

    private static void Write32(MemoryStream ms, uint value)
    {
        Span<byte> b = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(b, value);
        ms.Write(b);
    }

    private static void Write64(MemoryStream ms, ulong value)
    {
        Span<byte> b = stackalloc byte[8];
        BinaryPrimitives.WriteUInt64LittleEndian(b, value);
        ms.Write(b);
    }
}
