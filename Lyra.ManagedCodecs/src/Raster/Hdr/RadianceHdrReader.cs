using System.Text;

namespace Lyra.ManagedCodecs.Raster.Hdr;

/// <summary>
/// A pure-managed reader for Radiance RGBE images (the <c>.hdr</c> / <c>.pic</c> format
/// developed by Greg Ward). Handles <c>32-bit_rle_rgbe</c> and <c>32-bit_rle_xyze</c> in both the
/// new-style run-length encoded layout and the old flat layout, returning linear RGBA float
/// pixels with top-left origin. The <c>GAMMA</c>/<c>EXPOSURE</c> header fields are not applied,
/// matching the behavior of the native reference reader this replaces.
/// </summary>
public sealed class RadianceHdrReader
{
    private const string FormatPrefix = "FORMAT=";

    // CIE XYZ to Radiance's RGB (its standard primaries, equal-energy white), as Radiance's own
    // xyz2rgbmat, so an XYZE white comes out the same RGB white an RGBE one does.
    private static readonly float[] XyzToRgb =
    [
        2.565313f, -1.166850f, -0.398463f,
        -1.022108f, 1.978287f, 0.043822f,
        0.074724f, -0.251940f, 1.177215f,
    ];

    // Run-length encoding only applies to scanlines in this width range; outside it the file
    // is stored flat (one RGBE quad per pixel, no per-scanline header).
    private const int MinRleWidth = 8;
    private const int MaxRleWidth = 0x7fff;

    // rgbe2float scale per exponent byte: 2^(e-136), with e==0 mapped to 0 (black). Precomputed so
    // the per-pixel conversion is a table lookup and a multiply, with no MathF.ScaleB call or branch.
    private static readonly float[] ExpTable = BuildExpTable();

    private static float[] BuildExpTable()
    {
        var table = new float[256];
        for (var e = 1; e < 256; e++)
        {
            table[e] = MathF.ScaleB(1f, e - (128 + 8));
        }

        return table;
    }

    public bool CanDecode(ReadOnlySpan<byte> header) => IsLikelyHdr(header);

    public DecodedFloatImage Decode(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        return Decode(ReadAllBytes(stream));
    }

    /// <summary>Decodes a Radiance HDR image already resident in memory.</summary>
    public static DecodedFloatImage Decode(ReadOnlySpan<byte> data)
    {
        var reader = new ByteReader(data);

        ParseHeader(ref reader, out var width, out var height, out var flipX, out var flipY, out var xyz);

        if (width <= 0 || height <= 0)
        {
            throw new InvalidDataException($"HDR: invalid dimensions {width}x{height}.");
        }

        var pixels = new float[checked((long)width * height * 4)];
        DecodePixels(ref reader, width, height, flipX, flipY, pixels);

        if (xyz)
            ConvertXyzToRgb(pixels);

        return new DecodedFloatImage(pixels, width, height);
    }

    // --------------------------------------------------------
    //  Header
    // --------------------------------------------------------

    private static void ParseHeader(ref ByteReader reader, out int width, out int height, out bool flipX, out bool flipY, out bool xyz)
    {
        string? format = null;

        for (var line = reader.ReadLine(); line.Length > 0; line = reader.ReadLine())
        {
            if (line.StartsWith(FormatPrefix, StringComparison.Ordinal))
                format = line[FormatPrefix.Length..].Trim();
        }

        xyz = format switch
        {
            "32-bit_rle_rgbe" => false,
            "32-bit_rle_xyze" => true,
            null => throw new InvalidDataException("HDR: no FORMAT specifier found in header."),
            _ => throw new NotSupportedException($"HDR: unsupported FORMAT \"{format}\".")
        };

        ParseResolution(reader.ReadLine(), out width, out height, out flipX, out flipY);
    }

    private static void ConvertXyzToRgb(float[] pixels)
    {
        var m = XyzToRgb;

        for (var i = 0; i < pixels.Length; i += 4)
        {
            float x = pixels[i], y = pixels[i + 1], z = pixels[i + 2];
            pixels[i] = (m[0] * x) + (m[1] * y) + (m[2] * z);
            pixels[i + 1] = (m[3] * x) + (m[4] * y) + (m[5] * z);
            pixels[i + 2] = (m[6] * x) + (m[7] * y) + (m[8] * z);
        }
    }

    /// <summary>
    /// Parses the resolution line, e.g. <c>-Y 768 +X 1024</c>. The first axis encodes rows
    /// (height) and the second columns (width); a <c>+Y</c> sign means the first stored
    /// scanline is the visual bottom (vertical flip) and a <c>-X</c> sign means pixels run
    /// right-to-left (horizontal flip). Rotated layouts (X axis first) are not supported.
    /// </summary>
    private static void ParseResolution(string line, out int width, out int height, out bool flipX, out bool flipY)
    {
        var parts = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 4)
        {
            throw new InvalidDataException($"HDR: malformed resolution line \"{line}\".");
        }

        ParseAxis(parts[0], out var axis1, out var sign1);
        ParseAxis(parts[2], out var axis2, out var sign2);

        if (axis1 != 'Y' || axis2 != 'X')
        {
            throw new NotSupportedException($"HDR: unsupported resolution orientation \"{line}\" (only -Y/+X layouts).");
        }

        if (!int.TryParse(parts[1], out height) || !int.TryParse(parts[3], out width))
        {
            throw new InvalidDataException($"HDR: non-numeric image size in \"{line}\".");
        }

        flipY = sign1 == '+';
        flipX = sign2 == '-';
    }

    private static void ParseAxis(string token, out char axis, out char sign)
    {
        if (token.Length != 2 || (token[0] != '+' && token[0] != '-') || (token[1] != 'X' && token[1] != 'Y'))
        {
            throw new InvalidDataException($"HDR: malformed axis token \"{token}\".");
        }

        sign = token[0];
        axis = token[1];
    }

    // --------------------------------------------------------
    //  Pixel reading
    // --------------------------------------------------------

    private static void DecodePixels(ref ByteReader reader, int width, int height, bool flipX, bool flipY, float[] pixels)
    {
        if (width is < MinRleWidth or > MaxRleWidth)
        {
            // Widths outside the RLE range are always stored flat.
            DecodeFlat(ref reader, width, height, 0, flipX, flipY, pixels);
            return;
        }

        var plane = new byte[width * 4];

        for (var y = 0; y < height; y++)
        {
            var b0 = reader.ReadByte();
            var b1 = reader.ReadByte();
            var b2 = reader.ReadByte();
            var b3 = reader.ReadByte();

            // New-style RLE scanlines start with the marker 0x02 0x02 followed by the 15-bit
            // scanline width. Anything else means this is an old flat file: the four bytes we
            // just read are a real pixel, and the remainder of the image is stored flat.
            if (b0 != 2 || b1 != 2 || (b2 & 0x80) != 0)
            {
                WritePixel(pixels, width, height, flipX, flipY, y, 0, b0, b1, b2, b3);
                DecodeFlat(ref reader, width, height, (y * width) + 1, flipX, flipY, pixels);
                return;
            }

            if (((b2 << 8) | b3) != width)
            {
                throw new InvalidDataException("HDR: RLE scanline width does not match image width.");
            }

            // Each of the four channels (R, G, B, E) is run-length encoded separately into its own
            // plane, then de-interleaved straight into float pixels (no intermediate RGBE buffer).
            for (var c = 0; c < 4; c++)
            {
                DecodeRlePlane(ref reader, plane, c * width, width);
            }

            var destY = flipY ? height - 1 - y : y;
            var rowBase = destY * width;
            for (var x = 0; x < width; x++)
            {
                var destX = flipX ? width - 1 - x : x;
                var dst = (rowBase + destX) * 4;
                var f = ExpTable[plane[(3 * width) + x]];

                pixels[dst] = plane[x] * f;
                pixels[dst + 1] = plane[width + x] * f;
                pixels[dst + 2] = plane[(2 * width) + x] * f;
                pixels[dst + 3] = 1f;
            }
        }
    }

    /// <summary>Decodes one run-length encoded channel plane of <paramref name="count"/> bytes.</summary>
    private static void DecodeRlePlane(ref ByteReader reader, byte[] plane, int offset, int count)
    {
        var end = offset + count;
        var pos = offset;

        while (pos < end)
        {
            var control = reader.ReadByte();
            if (control > 128)
            {
                // A run: the next byte repeated (control - 128) times.
                var runLength = control - 128;
                if (runLength == 0 || runLength > end - pos)
                {
                    throw new InvalidDataException("HDR: bad RLE run length.");
                }

                var value = reader.ReadByte();
                for (var i = 0; i < runLength; i++)
                {
                    plane[pos++] = value;
                }
            }
            else
            {
                // A literal: `control` distinct bytes copied verbatim.
                if (control == 0 || control > end - pos)
                {
                    throw new InvalidDataException("HDR: bad RLE literal length.");
                }

                reader.ReadExact(plane.AsSpan(pos, control));
                pos += control;
            }
        }
    }

    /// <summary>
    /// Reads flat (uncompressed) RGBE pixels from <paramref name="firstPixel"/> to the end of the
    /// image, converting each to float in place. Used for old-format files and for widths outside
    /// the RLE range.
    /// </summary>
    private static void DecodeFlat(ref ByteReader reader, int width, int height, int firstPixel, bool flipX, bool flipY, float[] pixels)
    {
        var total = width * height;
        Span<byte> quad = stackalloc byte[4];

        for (var p = firstPixel; p < total; p++)
        {
            reader.ReadExact(quad);
            WritePixel(pixels, width, height, flipX, flipY, p / width, p % width, quad[0], quad[1], quad[2], quad[3]);
        }
    }

    /// <summary>Converts one RGBE pixel to float and stores it at its (flipped) destination.</summary>
    private static void WritePixel(float[] pixels, int width, int height, bool flipX, bool flipY, int y, int x, byte r, byte g, byte b, byte e)
    {
        var destY = flipY ? height - 1 - y : y;
        var destX = flipX ? width - 1 - x : x;
        var dst = (destY * width + destX) * 4;
        var f = ExpTable[e];

        pixels[dst] = r * f;
        pixels[dst + 1] = g * f;
        pixels[dst + 2] = b * f;
        pixels[dst + 3] = 1f;
    }

    // --------------------------------------------------------
    //  Detection & helpers
    // --------------------------------------------------------

    /// <summary>Radiance files begin with the <c>#?</c> magic token (e.g. <c>#?RADIANCE</c>).</summary>
    internal static bool IsLikelyHdr(ReadOnlySpan<byte> header)
        => header.Length >= 2 && header[0] == (byte)'#' && header[1] == (byte)'?';

    private static byte[] ReadAllBytes(Stream stream)
    {
        if (stream is MemoryStream ms)
        {
            return ms.ToArray();
        }

        using MemoryStream buffer = new();
        stream.CopyTo(buffer);
        return buffer.ToArray();
    }

    /// <summary>Cursor over the source bytes, used for both header lines and pixel data.</summary>
    private ref struct ByteReader
    {
        private readonly ReadOnlySpan<byte> _data;
        private int _pos;

        public ByteReader(ReadOnlySpan<byte> data)
        {
            _data = data;
            _pos = 0;
        }

        public byte ReadByte()
        {
            if (_pos >= _data.Length)
            {
                throw new InvalidDataException("HDR: unexpected end of pixel data.");
            }

            return _data[_pos++];
        }

        public void ReadExact(scoped Span<byte> destination)
        {
            if (_pos + destination.Length > _data.Length)
            {
                throw new InvalidDataException("HDR: unexpected end of pixel data.");
            }

            _data.Slice(_pos, destination.Length).CopyTo(destination);
            _pos += destination.Length;
        }

        /// <summary>Reads one newline-terminated ASCII line, stripping a trailing CR/LF.</summary>
        public string ReadLine()
        {
            var start = _pos;
            while (_pos < _data.Length && _data[_pos] != (byte)'\n')
            {
                _pos++;
            }

            if (_pos >= _data.Length)
            {
                throw new InvalidDataException("HDR: unexpected end of header.");
            }

            var end = _pos;
            _pos++; // consume '\n'

            if (end > start && _data[end - 1] == (byte)'\r')
            {
                end--;
            }

            return Encoding.ASCII.GetString(_data.Slice(start, end - start));
        }
    }
}