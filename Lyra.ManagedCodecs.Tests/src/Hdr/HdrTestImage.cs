using System.Text;

namespace Lyra.ManagedCodecs.Tests.Hdr;

/// <summary>
/// Builds in-memory Radiance HDR (RGBE) files for decoder tests: a standard header plus either
/// flat (uncompressed) pixels or new-style run-length encoded scanlines.
/// </summary>
internal static class HdrTestImage
{
    /// <summary>Writes the standard header and resolution line (e.g. <c>-Y 2 +X 2</c>).</summary>
    public static List<byte> Header(int width, int height, char ySign = '-', char xSign = '+')
    {
        var bytes = new List<byte>();
        Ascii(bytes, "#?RADIANCE\n");
        Ascii(bytes, "GAMMA=1.0\n");
        Ascii(bytes, "FORMAT=32-bit_rle_rgbe\n");
        Ascii(bytes, "\n");
        Ascii(bytes, $"{ySign}Y {height} {xSign}X {width}\n");
        return bytes;
    }

    /// <summary>A flat (uncompressed) image: RGBE quads in stored scanline order.</summary>
    public static byte[] Flat(int width, int height, char ySign, char xSign, params (byte R, byte G, byte B, byte E)[] pixels)
    {
        var bytes = Header(width, height, ySign, xSign);
        foreach (var (r, g, b, e) in pixels)
        {
            bytes.Add(r);
            bytes.Add(g);
            bytes.Add(b);
            bytes.Add(e);
        }

        return bytes.ToArray();
    }

    /// <summary>The 4-byte new-RLE scanline marker: <c>0x02 0x02</c> followed by the 15-bit width.</summary>
    public static void AppendRleMarker(List<byte> bytes, int width)
    {
        bytes.Add(2);
        bytes.Add(2);
        bytes.Add((byte)(width >> 8));
        bytes.Add((byte)(width & 0xFF));
    }

    /// <summary>Encodes a channel plane as RLE literal packets (no runs); always valid.</summary>
    public static void AppendLiteralPlane(List<byte> bytes, ReadOnlySpan<byte> values)
    {
        var pos = 0;
        while (pos < values.Length)
        {
            var chunk = Math.Min(128, values.Length - pos);
            bytes.Add((byte)chunk); // literal control byte, 1..128
            for (var i = 0; i < chunk; i++)
            {
                bytes.Add(values[pos + i]);
            }

            pos += chunk;
        }
    }

    /// <summary>Encodes a channel plane as a single RLE run of <paramref name="count"/> copies.</summary>
    public static void AppendRunPlane(List<byte> bytes, byte value, int count)
    {
        bytes.Add((byte)(128 + count)); // run control byte, >128
        bytes.Add(value);
    }

    private static void Ascii(List<byte> bytes, string text) => bytes.AddRange(Encoding.ASCII.GetBytes(text));
}
