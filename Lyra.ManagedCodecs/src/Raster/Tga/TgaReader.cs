namespace Lyra.ManagedCodecs.Raster.Tga;

/// <summary>
/// A pure-managed TGA (Truevision Targa) decoder. Supports uncompressed and run-length
/// encoded variants of grayscale (8/16-bit), true-color (15/16/24/32-bit) and color-mapped
/// images, with all four image origins. Output is always 8-bit RGBA, top-left origin.
/// </summary>
public static class TgaReader
{
    /// <summary>Cheap header sniff: whether <paramref name="header"/> plausibly begins a TGA file.</summary>
    public static bool CanDecode(ReadOnlySpan<byte> header) => IsLikelyTga(header);

    /// <summary>The 18-byte header at the start of <paramref name="data"/>.</summary>
    public static TgaHeader ReadHeader(ReadOnlySpan<byte> data) =>
        data.Length >= TgaHeader.Size ? TgaHeader.Parse(data) : throw new InvalidDataException("File is too small to contain a header.");

    /// <summary>Whether the file ends in the TGA 2.0 footer, which version 1 files do not have.</summary>
    public static bool IsVersion2(ReadOnlySpan<byte> data) =>
        data.Length >= TgaHeader.Size + FooterSize && data[^(Signature.Length)..].SequenceEqual(Signature);

    // Extension and developer area offsets, then the signature.
    private const int FooterSize = 26;

    private static ReadOnlySpan<byte> Signature => "TRUEVISION-XFILE.\0"u8;

    /// <summary>Decodes a TGA already resident in memory.</summary>
    public static DecodedImage Decode(ReadOnlySpan<byte> data)
    {
        var image = Decode(data, out var truncated);

        return truncated ? throw new InvalidDataException("Unexpected end of file while reading pixel data.") : image;
    }

    /// <summary>
    /// Decodes a TGA already resident in memory, as far as its pixel data goes: the pixels a short file
    /// does not reach are left transparent, and <paramref name="truncated"/> says so.
    /// </summary>
    public static DecodedImage Decode(ReadOnlySpan<byte> data, out bool truncated)
    {
        if (data.Length < TgaHeader.Size)
        {
            throw new InvalidDataException("File is too small to contain a header.");
        }

        var header = TgaHeader.Parse(data);

        if (header.ColorMapType is not 0 and not 1)
        {
            throw new NotSupportedException($"Unsupported color map type {header.ColorMapType}.");
        }

        if (header.Width == 0 || header.Height == 0)
        {
            throw new InvalidDataException("Width and height must be non-zero.");
        }

        if (!header.ImageType.IsValid() || header.ImageType == TgaImageType.NoImageData)
        {
            throw new NotSupportedException($"Unsupported image type {header.ImageType}.");
        }

        int width = header.Width;
        int height = header.Height;
        int pos = TgaHeader.Size;

        // Skip the optional image id field.
        pos = Advance(data, pos, header.IdLength);

        // True-color 32-bit is assumed to carry an 8-bit alpha channel even when the descriptor
        // lies about its attribute bits, which some encoders do.
        bool trueColor32 = header.PixelDepth == 32 &&
                           header.ImageType is TgaImageType.TrueColor or TgaImageType.RleTrueColor;
        int alphaBits = trueColor32 ? 8 : header.AttributeBits;
        bool hasAlpha = alphaBits > 0;

        // Read or skip the color map.
        ReadOnlySpan<byte> palette = default;
        int paletteEntrySize = 0;
        if (header.CMapLength > 0)
        {
            paletteEntrySize = header.CMapDepth / 8;
            if (paletteEntrySize is < 1 or > 4)
            {
                throw new NotSupportedException($"Unsupported color map entry depth {header.CMapDepth}.");
            }

            int paletteBytes = header.CMapLength * paletteEntrySize;
            if (header.ColorMapType == 1)
            {
                palette = Slice(data, pos, paletteBytes, "color map");
            }

            pos = Advance(data, pos, paletteBytes);
        }

        bool paletted = header.ImageType.IsColorMapped();
        if (paletted)
        {
            if (header.ColorMapType != 1 || palette.IsEmpty)
            {
                throw new InvalidDataException("Color-mapped image is missing its color map.");
            }

            if (header.PixelDepth != 8)
            {
                throw new NotSupportedException($"Only 8-bit color map indices are supported (got {header.PixelDepth}-bit).");
            }
        }

        int bytesPerPixel = paletted ? 1 : BytesPerPixel(header.PixelDepth);
        if (bytesPerPixel == 0)
        {
            throw new NotSupportedException($"Unsupported pixel depth {header.PixelDepth}.");
        }

        long rawLength = (long)width * height * bytesPerPixel;

        // Source pixel data, either read directly or decompressed from RLE packets; a short file
        // fills only its start.
        ReadOnlySpan<byte> raw;
        long rawRead;
        if (header.ImageType.IsRunLengthEncoded())
        {
            var rawOwner = new byte[rawLength];
            rawRead = DecompressRle(data, pos, rawOwner, bytesPerPixel);
            raw = rawOwner;
        }
        else if (data.Length - pos >= rawLength)
        {
            raw = data.Slice(pos, checked((int)rawLength));
            rawRead = rawLength;
        }
        else
        {
            var rawOwner = new byte[rawLength];
            rawRead = Math.Max(0, data.Length - pos);
            data[pos..].CopyTo(rawOwner);
            raw = rawOwner;
        }

        long pixelsRead = rawRead / bytesPerPixel;
        if (pixelsRead == 0)
        {
            throw new InvalidDataException("The file ends before its pixel data.");
        }

        truncated = pixelsRead < (long)width * height;

        byte[] rgba = new byte[(long)width * height * 4];

        bool flipX = header.Origin is TgaImageOrigin.TopRight or TgaImageOrigin.BottomRight;
        bool flipY = header.Origin is TgaImageOrigin.BottomLeft or TgaImageOrigin.BottomRight;

        bool grayscale16 = header.PixelDepth is 15 or 16 && header.ImageType.IsGrayscale();

        for (int y = 0; y < height; y++)
        {
            int destY = flipY ? height - 1 - y : y;
            int srcRow = y * width * bytesPerPixel;
            int destRow = destY * width * 4;

            for (int x = 0; x < width; x++)
            {
                // Pixels a short file never reached stay transparent.
                if ((long)y * width + x >= pixelsRead)
                {
                    break;
                }

                int destX = flipX ? width - 1 - x : x;
                int src = srcRow + (x * bytesPerPixel);
                int dst = destRow + (destX * 4);

                byte r, g, b, a;
                if (paletted)
                {
                    int index = raw[src];
                    if (index >= header.CMapLength)
                    {
                        index = 0;
                    }

                    ExpandPaletteEntry(palette, index * paletteEntrySize, paletteEntrySize, hasAlpha, out r, out g, out b, out a);
                }
                else
                {
                    switch (bytesPerPixel)
                    {
                        case 1: // 8-bit grayscale
                            r = g = b = raw[src];
                            a = 255;
                            break;
                        case 2 when grayscale16: // 16-bit grayscale + alpha (La16)
                            r = g = b = raw[src];
                            a = hasAlpha ? raw[src + 1] : (byte)255;
                            break;
                        case 2: // 15/16-bit true-color (Bgra5551)
                            ExpandBgra5551(raw[src], raw[src + 1], hasAlpha, out r, out g, out b, out a);
                            break;
                        case 3: // 24-bit BGR
                            b = raw[src];
                            g = raw[src + 1];
                            r = raw[src + 2];
                            a = 255;
                            break;
                        default: // 32-bit BGRA
                            b = raw[src];
                            g = raw[src + 1];
                            r = raw[src + 2];
                            a = hasAlpha ? raw[src + 3] : (byte)255;
                            break;
                    }
                }

                rgba[dst] = r;
                rgba[dst + 1] = g;
                rgba[dst + 2] = b;
                rgba[dst + 3] = a;
            }
        }

        return new DecodedImage(rgba, width, height);
    }

    private static void ExpandPaletteEntry(ReadOnlySpan<byte> palette, int offset, int entrySize, bool hasAlpha, out byte r, out byte g, out byte b, out byte a)
    {
        switch (entrySize)
        {
            case 1:
                r = g = b = palette[offset];
                a = 255;
                break;
            case 2:
                ExpandBgra5551(palette[offset], palette[offset + 1], hasAlpha, out r, out g, out b, out a);
                break;
            case 3:
                b = palette[offset];
                g = palette[offset + 1];
                r = palette[offset + 2];
                a = 255;
                break;
            default:
                b = palette[offset];
                g = palette[offset + 1];
                r = palette[offset + 2];
                a = palette[offset + 3];
                break;
        }
    }

    /// <summary>Expands a little-endian 5-5-5-1 packed BGRA pixel to 8-bit channels.</summary>
    private static void ExpandBgra5551(byte low, byte high, bool hasAlpha, out byte r, out byte g, out byte b, out byte a)
    {
        int packed = low | (high << 8);
        b = Expand5(packed & 0x1F);
        g = Expand5((packed >> 5) & 0x1F);
        r = Expand5((packed >> 10) & 0x1F);
        a = !hasAlpha || ((packed >> 15) & 0x1) == 1 ? (byte)255 : (byte)0;
    }

    /// <summary>Scales a 5-bit channel to 8 bits, preserving full black/white.</summary>
    private static byte Expand5(int v) => (byte)((v << 3) | (v >> 2));

    /// <summary>
    /// Expands run-length encoded pixel data into <paramref name="dest"/>. Each packet starts
    /// with a header byte: the high bit marks a run (repeat one pixel) versus a literal packet
    /// (copy N distinct pixels); the low 7 bits hold the count minus one.
    /// </summary>
    /// <summary>Expands RLE packets into <paramref name="dest"/>, returning how many bytes the data filled before it ended.</summary>
    private static int DecompressRle(ReadOnlySpan<byte> data, int pos, Span<byte> dest, int bytesPerPixel)
    {
        int written = 0;
        while (written < dest.Length && pos < data.Length)
        {
            byte packet = data[pos++];
            int count = (packet & 0x7F) + 1;
            int chunk = count * bytesPerPixel;

            if (written + chunk > dest.Length)
            {
                throw new InvalidDataException("RLE data overruns the image dimensions.");
            }

            if ((packet & 0x80) != 0)
            {
                if (pos + bytesPerPixel > data.Length)
                {
                    break;
                }

                ReadOnlySpan<byte> pixel = data.Slice(pos, bytesPerPixel);
                pos += bytesPerPixel;
                for (int i = 0; i < count; i++)
                {
                    pixel.CopyTo(dest.Slice(written, bytesPerPixel));
                    written += bytesPerPixel;
                }
            }
            else
            {
                int available = Math.Min(chunk, data.Length - pos);
                data.Slice(pos, available).CopyTo(dest.Slice(written, available));
                pos += available;
                written += available;
            }
        }

        return written;
    }

    /// <summary>Bytes per stored pixel for a given true-color/grayscale pixel depth.</summary>
    private static int BytesPerPixel(int pixelDepth) => pixelDepth switch
    {
        8 => 1,
        15 or 16 => 2,
        24 => 3,
        32 => 4,
        _ => 0,
    };

    private static int Advance(ReadOnlySpan<byte> data, int pos, int count)
    {
        long next = (long)pos + count;
        if (next > data.Length)
        {
            throw new InvalidDataException("Unexpected end of file.");
        }

        return (int)next;
    }

    private static ReadOnlySpan<byte> Slice(ReadOnlySpan<byte> data, int pos, int count, string what)
    {
        if (pos < 0 || count < 0 || (long)pos + count > data.Length)
        {
            throw new InvalidDataException($"Unexpected end of file while reading {what} data.");
        }

        return data.Slice(pos, count);
    }

    /// <summary>
    /// TGA has no magic number, so detection validates the structural invariants of the header.
    /// Mirrors the heuristic used by mainstream decoders.
    /// </summary>
    internal static bool IsLikelyTga(ReadOnlySpan<byte> header)
    {
        if (header.Length < 16)
        {
            return false;
        }

        // Color map type must be 0 or 1.
        if (header[1] is not 0 and not 1)
        {
            return false;
        }

        // Image type must be a known value.
        if (!((TgaImageType)header[2]).IsValid())
        {
            return false;
        }

        // When no color map is present, the color map specification must be all zeros.
        if (header[1] == 0)
        {
            if (header[3] != 0 || header[4] != 0 || header[5] != 0 || header[6] != 0 || header[7] != 0)
            {
                return false;
            }
        }

        // Width and height must be non-zero.
        if ((header[12] == 0 && header[13] == 0) || (header[14] == 0 && header[15] == 0))
        {
            return false;
        }

        return true;
    }

}