using System.Buffers.Binary;

namespace Lyra.ManagedCodecs.Tests.Qoi;

/// <summary>
/// A port of the reference encoder (qoi.h), so round trips exercise every chunk the way real
/// files use them, plus helpers that build images likely to need each one.
/// </summary>
internal static class QoiTestImage
{
    public const byte OpRgb = 0xFE, OpRgba = 0xFF, OpIndex = 0x00, OpDiff = 0x40, OpLuma = 0x80, OpRun = 0xC0;

    public static byte[] Encode(byte[] rgba, int width, int height, byte channels = 4, byte colorSpace = 0)
    {
        var output = new List<byte>();
        output.AddRange("qoif"u8.ToArray());

        var size = new byte[8];
        BinaryPrimitives.WriteUInt32BigEndian(size, (uint)width);
        BinaryPrimitives.WriteUInt32BigEndian(size.AsSpan(4), (uint)height);
        output.AddRange(size);
        output.Add(channels);
        output.Add(colorSpace);

        var index = new (byte R, byte G, byte B, byte A)[64];
        (byte R, byte G, byte B, byte A) prev = (0, 0, 0, 255);
        var run = 0;
        var total = width * height;

        for (var i = 0; i < total; i++)
        {
            var px = (rgba[i * 4], rgba[i * 4 + 1], rgba[i * 4 + 2], channels == 4 ? rgba[i * 4 + 3] : (byte)255);

            if (px == prev)
            {
                run++;
                if (run == 62 || i == total - 1)
                {
                    output.Add((byte)(OpRun | (run - 1)));
                    run = 0;
                }

                continue;
            }

            if (run > 0)
            {
                output.Add((byte)(OpRun | (run - 1)));
                run = 0;
            }

            var hash = (px.Item1 * 3 + px.Item2 * 5 + px.Item3 * 7 + px.Item4 * 11) % 64;

            if (index[hash] == px)
            {
                output.Add((byte)(OpIndex | hash));
            }
            else
            {
                index[hash] = px;

                if (px.Item4 == prev.A)
                {
                    var vr = (sbyte)(px.Item1 - prev.R);
                    var vg = (sbyte)(px.Item2 - prev.G);
                    var vb = (sbyte)(px.Item3 - prev.B);
                    var vgR = vr - vg;
                    var vgB = vb - vg;

                    if (vr is >= -2 and <= 1 && vg is >= -2 and <= 1 && vb is >= -2 and <= 1)
                        output.Add((byte)(OpDiff | (vr + 2) << 4 | (vg + 2) << 2 | (vb + 2)));
                    else if (vgR is >= -8 and <= 7 && vg is >= -32 and <= 31 && vgB is >= -8 and <= 7)
                        output.AddRange([(byte)(OpLuma | (vg + 32)), (byte)((vgR + 8) << 4 | (vgB + 8))]);
                    else
                        output.AddRange([OpRgb, px.Item1, px.Item2, px.Item3]);
                }
                else
                {
                    output.AddRange([OpRgba, px.Item1, px.Item2, px.Item3, px.Item4]);
                }
            }

            prev = px;
        }

        output.AddRange([0, 0, 0, 0, 0, 0, 0, 1]);
        return [.. output];
    }

    /// <summary>
    /// Smooth gradients (DIFF, LUMA), flat runs (RUN), colors that come back (INDEX), hard jumps
    /// (RGB) and alpha changes (RGBA), all in one deterministic image.
    /// </summary>
    public static byte[] Varied(int width, int height)
    {
        var rgba = new byte[width * height * 4];

        for (var y = 0; y < height; y++)
        for (var x = 0; x < width; x++)
        {
            var i = (y * width + x) * 4;
            (byte, byte, byte, byte) px = (y % 4) switch
            {
                0 => ((byte)x, (byte)(x / 2), (byte)(255 - x), 255),
                1 => x < width / 2 ? ((byte)40, (byte)80, (byte)120, (byte)255) : ((byte)(x * 37), (byte)(x * 91), (byte)(x * 13), (byte)255),
                2 => ((byte)(x % 3 * 100), (byte)(x % 3 * 50), (byte)(x % 3 * 25), (byte)255),
                _ => ((byte)x, (byte)y, (byte)(x + y), (byte)(x * 16)),
            };

            (rgba[i], rgba[i + 1], rgba[i + 2], rgba[i + 3]) = px;
        }

        return rgba;
    }
}