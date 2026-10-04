using System.Buffers.Binary;

namespace Lyra.ManagedCodecs.Raster.Qoi;

/// <summary>
/// A pure-managed QOI ("Quite OK Image") decoder. Output is 8-bit RGBA, unpremultiplied,
/// top-left origin.
/// </summary>
public static class QoiReader
{
    /// <summary>The reference decoder's ceiling, which keeps the RGBA output within a single span.</summary>
    public const long MaxPixels = 400_000_000;

    private const byte OpRgb   = 0xFE;
    private const byte OpRgba  = 0xFF;
    private const byte OpIndex = 0x00;
    private const byte OpDiff  = 0x40;
    private const byte OpLuma  = 0x80;
    private const byte OpRun   = 0xC0;
    private const byte TagMask = 0xC0;

    private const int CancellationStride = 1 << 20;

    private static ReadOnlySpan<byte> Magic => "qoif"u8;
    private static ReadOnlySpan<byte> EndMarker => [0, 0, 0, 0, 0, 0, 0, 1];
    
    public static bool CanDecode(ReadOnlySpan<byte> data) 
        => data.Length >= Magic.Length && data[..Magic.Length].SequenceEqual(Magic);

    public static QoiHeader ReadHeader(ReadOnlySpan<byte> data)
    {
        if (data.Length < QoiHeader.Size)
        {
            throw new InvalidDataException("QOI: file is too small to contain a header.");
        }

        if (!CanDecode(data))
        {
            throw new InvalidDataException("QOI: the file does not start with the QOI signature.");
        }

        var header = new QoiHeader(
            BinaryPrimitives.ReadUInt32BigEndian(data[4..]),
            BinaryPrimitives.ReadUInt32BigEndian(data[8..]),
            data[12],
            (QoiColorSpace)data[13]);

        if (header.Width == 0 || header.Height == 0)
        {
            throw new InvalidDataException("QOI: width and height must be non-zero.");
        }

        if (header.Channels is not (3 or 4))
        {
            throw new InvalidDataException($"QOI: invalid channel count {header.Channels}.");
        }

        if (header.ColorSpace is not (QoiColorSpace.SrgbLinearAlpha or QoiColorSpace.Linear))
        {
            throw new InvalidDataException($"QOI: invalid color space {data[13]}.");
        }

        return header;
    }

    /// <summary>
    /// Decodes into <paramref name="rgba"/>, tightly packed rows of <c>Width * 4</c> bytes. Returns
    /// how many pixels the data held: fewer than <see cref="QoiHeader.PixelCount"/> when it ends
    /// early, in which case the rest is cleared to transparent.
    /// </summary>
    public static long DecodeInto(ReadOnlySpan<byte> data, QoiHeader header, Span<byte> rgba, CancellationToken ct = default)
    {
        if (header.PixelCount > MaxPixels)
        {
            throw new NotSupportedException($"QOI: {header.Width}x{header.Height} exceeds the {MaxPixels} pixel limit.");
        }

        var total = (int)header.PixelCount;
        if (rgba.Length < total * 4)
        {
            throw new ArgumentException($"QOI: the output holds {rgba.Length} bytes; {total * 4} are needed.", nameof(rgba));
        }

        // A file cut short has no end marker; then every byte after the header is chunk data.
        var end = data.Length;
        if (end >= QoiHeader.Size + EndMarker.Length && data[^EndMarker.Length..].SequenceEqual(EndMarker))
        {
            end -= EndMarker.Length;
        }

        Span<uint> index = stackalloc uint[64];
        index.Clear();

        byte r = 0, g = 0, b = 0, a = 255;
        var pos = QoiHeader.Size;
        var run = 0;
        var decoded = 0;

        for (; decoded < total; decoded++)
        {
            if ((decoded & (CancellationStride - 1)) == 0)
            {
                ct.ThrowIfCancellationRequested();
            }

            if (run > 0)
            {
                run--;
            }
            else
            {
                if (pos >= end)
                {
                    break;
                }

                var b1 = data[pos++];
                if (b1 == OpRgb)
                {
                    if (pos + 3 > end)
                    {
                        break;
                    }

                    r = data[pos];
                    g = data[pos + 1];
                    b = data[pos + 2];
                    pos += 3;
                }
                else if (b1 == OpRgba)
                {
                    if (pos + 4 > end)
                    {
                        break;
                    }

                    r = data[pos];
                    g = data[pos + 1];
                    b = data[pos + 2];
                    a = data[pos + 3];
                    pos += 4;
                }
                else
                {
                    // The one two-byte chunk among the tagged ones.
                    if ((b1 & TagMask) == OpLuma && pos >= end)
                    {
                        break;
                    }

                    switch (b1 & TagMask)
                    {
                        case OpIndex:
                            var px = index[b1];
                            r = (byte)px;
                            g = (byte)(px >> 8);
                            b = (byte)(px >> 16);
                            a = (byte)(px >> 24);
                            break;

                        case OpDiff:
                            r += (byte)(((b1 >> 4) & 0x03) - 2);
                            g += (byte)(((b1 >> 2) & 0x03) - 2);
                            b += (byte)((b1 & 0x03) - 2);
                            break;

                        case OpLuma:
                            var b2 = data[pos++];
                            var vg = (b1 & 0x3F) - 32;
                            r += (byte)(vg - 8 + ((b2 >> 4) & 0x0F));
                            g += (byte)vg;
                            b += (byte)(vg - 8 + (b2 & 0x0F));
                            break;

                        case OpRun:
                            run = b1 & 0x3F;
                            break;
                    }
                }

                index[(r * 3 + g * 5 + b * 7 + a * 11) % 64] = Pack(r, g, b, a);
            }

            BinaryPrimitives.WriteUInt32LittleEndian(rgba[(decoded * 4)..], Pack(r, g, b, a));
        }

        if (decoded < total)
        {
            rgba[(decoded * 4)..(total * 4)].Clear();
        }

        return decoded;
    }

    /// <summary>RGBA in memory order, read as a little-endian word.</summary>
    private static uint Pack(byte r, byte g, byte b, byte a) => r | (uint)g << 8 | (uint)b << 16 | (uint)a << 24;
}
