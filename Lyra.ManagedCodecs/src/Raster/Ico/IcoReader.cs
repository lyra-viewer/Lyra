using System.Buffers.Binary;

namespace Lyra.ManagedCodecs.Raster.Ico;

public enum IcoPayloadKind
{
    /// <summary>
    /// A Windows DIB: an info header, an optional palette, bottom-up pixel rows, and - unless the
    /// header says otherwise - a 1-bit AND mask underneath them.
    /// </summary>
    Dib,

    /// <summary>
    /// A whole image file stored as-is, which since Windows Vista means a PNG. Read here for its
    /// size and depth; decoded by whoever owns an image decoder.
    /// </summary>
    Embedded
}

public sealed record IcoEntry(int Width, int Height, int BitCount, IcoPayloadKind Kind, int PayloadOffset, int PayloadLength)
{
    public ReadOnlySpan<byte> Payload(byte[] data) => data.AsSpan(PayloadOffset, PayloadLength);
}

/// <summary>
/// A pure-managed reader for Windows icon containers (<c>.ico</c>, and the cursors that share
/// their layout). Every entry is a separately stored image with its own size, depth and encoding -
/// the 16x16 in an icon set was drawn by hand, not scaled from the 256x256 - so the container is
/// read as a set rather than as one image.
/// </summary>
public static class IcoReader
{
    private const int DirectoryHeaderSize = 6;
    private const int DirectoryEntrySize = 16;

    /// <summary>BITMAPINFOHEADER. Later headers (V4, V5) are longer and start the same way.</summary>
    private const int MinimumInfoHeaderSize = 40;

    /// <summary>BITMAPV5HEADER, the longest Windows defines.</summary>
    private const int MaximumInfoHeaderSize = 124;

    /// <summary>BI_RGB - the only compression an icon's DIB uses in practice.</summary>
    private const int Uncompressed = 0;

    /// <summary>A directory header and one entry, the least that can claim to be an icon.</summary>
    public const int MinimumLength = DirectoryHeaderSize + DirectoryEntrySize;

    /// <summary>
    /// Windows stops at 256, but PNG entries are whole files and nothing in the container caps
    /// them. Generous enough to read anything real, small enough that a corrupt header cannot ask
    /// for an enormous allocation.
    /// </summary>
    private const int MaximumDimension = 4096;

    public static bool IsIco(ReadOnlySpan<byte> data) =>
        data.Length >= MinimumLength
        && data[0] == 0 && data[1] == 0 // reserved
        && (data[2] == 1 || data[2] == 2) && data[3] == 0 // 1 = icon, 2 = cursor
        && BinaryPrimitives.ReadUInt16LittleEndian(data[4..]) > 0;

    /// <summary>
    /// The entries the container holds, largest first, then deepest - the order the sizes list
    /// shows them in, and the reason index 0 is the one worth opening on.
    /// </summary>
    public static IReadOnlyList<IcoEntry> ReadEntries(byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);

        if (!IsIco(data))
            return [];

        var count = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(4));
        var entries = new List<IcoEntry>(count);

        for (var i = 0; i < count; i++)
        {
            var offset = DirectoryHeaderSize + (i * DirectoryEntrySize);
            if (offset + DirectoryEntrySize > data.Length)
                break; // the directory claims more entries than the file holds

            // A zero dimension means 256: the field is one byte, so the largest icon Windows
            // defines cannot be written literally.
            var declaredWidth = data[offset] == 0 ? 256 : data[offset];
            var declaredHeight = data[offset + 1] == 0 ? 256 : data[offset + 1];

            // Meaningless in a cursor, where the same two bytes hold a hotspot coordinate.
            var declaredBits = data[2] == 1 ? BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(offset + 6)) : 0;

            var payloadLength = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(offset + 8));
            var payloadOffset = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(offset + 12));

            // In longs, so a length near uint.MaxValue cannot wrap back into the buffer.
            if (payloadLength == 0 || payloadOffset < DirectoryHeaderSize || (long)payloadOffset + payloadLength > data.Length)
                continue;

            var entry = Describe(data.AsSpan((int)payloadOffset, (int)payloadLength), declaredWidth, declaredHeight, declaredBits);

            if (entry is not null)
                entries.Add(entry with { PayloadOffset = (int)payloadOffset, PayloadLength = (int)payloadLength });
        }

        return
        [
            .. entries
                .OrderByDescending(entry => (long)entry.Width * entry.Height)
                .ThenByDescending(entry => entry.BitCount)
                .ThenByDescending(entry => entry.PayloadLength)
                .ThenBy(entry => entry.PayloadOffset)
        ];
    }

    /// <summary>
    /// Decodes a DIB entry to straight-alpha RGBA, top-down. Returns null for an entry that is not
    /// a DIB, or whose header and payload do not agree - <see cref="IcoPayloadKind.Embedded"/>
    /// payloads are whole image files and belong to an image decoder.
    /// </summary>
    public static DecodedImage? Decode(byte[] data, IcoEntry entry)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(entry);

        if (entry.Kind != IcoPayloadKind.Dib
            || entry.PayloadOffset < 0 || entry.PayloadLength < 0
            || (long)entry.PayloadOffset + entry.PayloadLength > data.Length)
            return null;

        var payload = entry.Payload(data);

        if (!TryReadDib(payload, entry.Height, out var header))
            return null;

        var stride = RowStride(header.Width, header.BitCount);
        var paletteBytes = header.PaletteCount * 4;
        var pixelStart = header.HeaderSize + paletteBytes;

        var pixelBytes = (long)stride * header.Height;
        if (pixelStart + pixelBytes > payload.Length)
            return null;

        var palette = header.PaletteCount > 0
            ? payload.Slice(header.HeaderSize, paletteBytes)
            : default;

        var maskStride = RowStride(header.Width, 1);
        var maskStart = pixelStart + (int)pixelBytes;
        var hasMask = header.HasMask && maskStart + ((long)maskStride * header.Height) <= payload.Length;
        var alphaFromPixels = header.BitCount == 32 && HasAnyAlpha(payload.Slice(pixelStart, (int)pixelBytes), stride, header.Width, header.Height);

        var pixels = new byte[(long)header.Width * header.Height * 4];

        for (var y = 0; y < header.Height; y++)
        {
            // DIB rows run bottom-up.
            var row = payload.Slice(pixelStart + ((header.Height - 1 - y) * stride), stride);
            var mask = hasMask
                ? payload.Slice(maskStart + ((header.Height - 1 - y) * maskStride), maskStride)
                : default;

            for (var x = 0; x < header.Width; x++)
            {
                var (r, g, b, a) = ReadPixel(row, palette, x, header.BitCount);

                if (!alphaFromPixels)
                    a = hasMask && IsMasked(mask, x) ? (byte)0x00 : (byte)0xFF;

                var target = ((y * header.Width) + x) * 4;
                pixels[target + 0] = r;
                pixels[target + 1] = g;
                pixels[target + 2] = b;
                pixels[target + 3] = a;
            }
        }

        return new DecodedImage(pixels, header.Width, header.Height);
    }
    
    private static IcoEntry? Describe(ReadOnlySpan<byte> payload, int declaredWidth, int declaredHeight, int declaredBits)
    {
        if (IsPng(payload))
        {
            var png = ReadPngHeader(payload);

            return png.Width > 0
                ? new IcoEntry(png.Width, png.Height, declaredBits > 0 ? declaredBits : png.BitCount, IcoPayloadKind.Embedded, 0, 0)
                : null;
        }

        // Any other whole-file payload - a JPEG, or something unrecognized - is still worth
        // publishing at the size the directory claims, since an image decoder may well-read it.
        if (!TryReadDib(payload, declaredHeight, out var header))
            return IsEmbeddable(payload) && InRange(declaredWidth) && InRange(declaredHeight)
                ? new IcoEntry(declaredWidth, declaredHeight, declaredBits, IcoPayloadKind.Embedded, 0, 0)
                : null;

        return new IcoEntry(header.Width, header.Height, header.BitCount, IcoPayloadKind.Dib, 0, 0);
    }

    private static bool TryReadDib(ReadOnlySpan<byte> payload, int declaredHeight, out DibHeader header)
    {
        header = default;

        if (payload.Length < MinimumInfoHeaderSize)
            return false;

        var headerSize = BinaryPrimitives.ReadUInt32LittleEndian(payload);
        if (headerSize is < MinimumInfoHeaderSize or > MaximumInfoHeaderSize || headerSize > (uint)payload.Length)
            return false;

        var width = BinaryPrimitives.ReadInt32LittleEndian(payload[4..]);
        var storedHeight = BinaryPrimitives.ReadInt32LittleEndian(payload[8..]);
        var bitCount = BinaryPrimitives.ReadUInt16LittleEndian(payload[14..]);
        var compression = BinaryPrimitives.ReadUInt32LittleEndian(payload[16..]);
        var paletteCount = BinaryPrimitives.ReadUInt32LittleEndian(payload[32..]);

        // BI_BITFIELDS and the rest put masks or a whole encoded image where the pixels should be.
        // Icons do not use them, and guessing at the layout would be worse than declining.
        if (compression != Uncompressed)
            return false;

        // The stored height covers the pixels and the mask beneath them, so it is twice the icon -
        // except in files that omit the mask and store the true height.
        var hasMask = storedHeight != declaredHeight;
        var height = hasMask ? storedHeight / 2 : storedHeight;

        if (!InRange(width) || !InRange(height))
            return false;

        if (bitCount is not (1 or 4 or 8 or 16 or 24 or 32))
            return false;

        // A palette is sized by the header, or implied by the depth when the header says nothing.
        var palette = bitCount <= 8
            ? paletteCount is > 0 and <= 256 ? (int)paletteCount : 1 << bitCount
            : 0;

        header = new DibHeader((int)headerSize, width, height, bitCount, palette, hasMask);
        return true;
    }

    /// <summary>
    /// What an embedded PNG declares in its IHDR, or a zero width when it declares nothing usable.
    /// </summary>
    private static PngHeader ReadPngHeader(ReadOnlySpan<byte> payload)
    {
        const int ihdr = 16;

        if (payload.Length < ihdr + 10 || !payload.Slice(12, 4).SequenceEqual("IHDR"u8))
            return default;

        var width = BinaryPrimitives.ReadInt32BigEndian(payload[ihdr..]);
        var height = BinaryPrimitives.ReadInt32BigEndian(payload[(ihdr + 4)..]);

        if (!InRange(width) || !InRange(height))
            return default;

        var depth = payload[ihdr + 8];
        var channels = payload[ihdr + 9] switch
        {
            0 => 1, // greyscale
            2 => 3, // truecolour
            3 => 1, // palette index
            4 => 2, // greyscale + alpha
            6 => 4, // truecolour + alpha
            _ => 0
        };

        return channels == 0 ? default : new PngHeader(width, height, depth * channels);
    }

    private static bool IsPng(ReadOnlySpan<byte> payload) =>
        payload.Length >= 8 && payload[0] == 0x89 && payload[1] == (byte)'P' && payload[2] == (byte)'N' && payload[3] == (byte)'G';

    private static bool IsEmbeddable(ReadOnlySpan<byte> payload) =>
        payload.Length >= 3 && payload[0] == 0xFF && payload[1] == 0xD8 && payload[2] == 0xFF; // JPEG

    private static bool InRange(int dimension) => dimension is > 0 and <= MaximumDimension;

    /// <summary>DIB rows are padded to a four-byte boundary.</summary>
    private static int RowStride(int width, int bitCount) => (((width * bitCount) + 31) / 32) * 4;

    private static bool HasAnyAlpha(ReadOnlySpan<byte> pixels, int stride, int width, int height)
    {
        for (var y = 0; y < height; y++)
        {
            var row = pixels.Slice(y * stride, stride);
            for (var x = 0; x < width; x++)
            {
                if (row[(x * 4) + 3] != 0)
                    return true;
            }
        }

        return false;
    }

    private static Rgba ReadPixel(ReadOnlySpan<byte> row, ReadOnlySpan<byte> palette, int x, int bitCount)
    {
        switch (bitCount)
        {
            case 32:
                return new Rgba(row[(x * 4) + 2], row[(x * 4) + 1], row[x * 4], row[(x * 4) + 3]);

            case 24:
                return new Rgba(row[(x * 3) + 2], row[(x * 3) + 1], row[x * 3], 0xFF);

            case 16:
            {
                // BI_RGB at 16 bits is X1R5G5B5.
                var value = BinaryPrimitives.ReadUInt16LittleEndian(row[(x * 2)..]);
                return new Rgba(Scale5((value >> 10) & 0x1F), Scale5((value >> 5) & 0x1F), Scale5(value & 0x1F), 0xFF);
            }

            case 8:
                return FromPalette(palette, row[x]);

            case 4:
                // Two pixels to a byte, the left one in the high nibble.
                return FromPalette(palette, (x & 1) == 0 ? row[x / 2] >> 4 : row[x / 2] & 0x0F);

            default:
                // One bit each, most significant first.
                return FromPalette(palette, (row[x / 8] >> (7 - (x & 7))) & 1);
        }
    }

    /// <summary>Palette entries are stored blue first, with a fourth byte that is not alpha.</summary>
    private static Rgba FromPalette(ReadOnlySpan<byte> palette, int index)
    {
        var offset = index * 4;

        return offset + 2 < palette.Length
            ? new Rgba(palette[offset + 2], palette[offset + 1], palette[offset], 0xFF)
            : new Rgba(0, 0, 0, 0xFF);
    }

    /// <summary>Set bits in the AND mask are the transparent ones.</summary>
    private static bool IsMasked(ReadOnlySpan<byte> mask, int x)
    {
        var index = x / 8;
        return index < mask.Length && ((mask[index] >> (7 - (x & 7))) & 1) == 1;
    }

    private static byte Scale5(int value) => (byte)((value * 255) / 31);

    private readonly record struct Rgba(byte R, byte G, byte B, byte A);

    private readonly record struct PngHeader(int Width, int Height, int BitCount);

    private readonly record struct DibHeader(int HeaderSize, int Width, int Height, int BitCount, int PaletteCount, bool HasMask);
}