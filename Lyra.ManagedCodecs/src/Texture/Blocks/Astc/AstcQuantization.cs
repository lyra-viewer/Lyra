using System.Numerics;

namespace Lyra.ManagedCodecs.Texture.Blocks.Astc;

/// <summary>
/// ASTC un-quantization (spec C.2.13 for color endpoints, C.2.17 for weights): turns a BISE-decoded
/// quantized value back into an endpoint component in [0,255] or a weight in [0,64]. A decoded value
/// <c>v = (trit/quint &lt;&lt; bits) | low</c> (see <see cref="AstcIntegerSequence"/>) splits by range into
/// its trit/quint digit and low bits, which feed the spec's A/B/C reconstruction.
///
/// The reconstruction matches Google's Apache-2.0 astc-codec; ranges are identified by their maximum
/// representable value (levels-1), the same descriptor the weight/endpoint decoders pass around.
/// </summary>
internal static class AstcQuantization
{
    /// <summary>Un-quantizes one color endpoint component to [0,255].</summary>
    public static int UnquantizeEndpoint(int value, int rangeMax)
    {
        var levels = rangeMax + 1;
        if (BitOperations.IsPow2(levels))
        {
            return Replicate(value, BitOperations.Log2((uint)levels), 8);
        }

        if (levels % 3 == 0 && BitOperations.IsPow2(levels / 3))
        {
            var nb = BitOperations.Log2((uint)(levels / 3));
            return UnquantTritEndpoint(value >> nb, value & ((1 << nb) - 1), rangeMax);
        }

        var qnb = BitOperations.Log2((uint)(levels / 5));
        return UnquantQuintEndpoint(value >> qnb, value & ((1 << qnb) - 1), rangeMax);
    }

    /// <summary>Un-quantizes one weight to [0,64].</summary>
    public static int UnquantizeWeight(int value, int rangeMax)
    {
        var levels = rangeMax + 1;
        int dq;
        if (BitOperations.IsPow2(levels))
        {
            dq = Replicate(value, BitOperations.Log2((uint)levels), 6);
        }
        else if (levels % 3 == 0 && BitOperations.IsPow2(levels / 3))
        {
            var nb = BitOperations.Log2((uint)(levels / 3));
            dq = UnquantTritWeight(value >> nb, value & ((1 << nb) - 1), rangeMax);
        }
        else
        {
            var nb = BitOperations.Log2((uint)(levels / 5));
            dq = UnquantQuintWeight(value >> nb, value & ((1 << nb) - 1), rangeMax);
        }

        // The spec reconstructs weights in [0,64) then maps the upper half to [0,64] (C.2.17).
        return dq > 32 ? dq + 1 : dq;
    }

    // ------------------------------------------------------------------
    //  Endpoint reconstruction (C.2.13) -> [0,255]
    // ------------------------------------------------------------------

    private static int UnquantTritEndpoint(int trit, int bits, int range)
    {
        var a = (bits & 1) != 0 ? 0x1FF : 0;
        int b, c;
        switch (range)
        {
            case 5:
                b = 0;
                c = 204;
                break;
            case 11:
            {
                var x = (bits >> 1) & 0x1;
                b = (x << 1) | (x << 2) | (x << 4) | (x << 8);
                c = 93;
            }
                break;
            case 23:
            {
                var x = (bits >> 1) & 0x3;
                b = x | (x << 2) | (x << 7);
                c = 44;
            }
                break;
            case 47:
            {
                var x = (bits >> 1) & 0x7;
                b = x | (x << 6);
                c = 22;
            }
                break;
            case 95:
            {
                var x = (bits >> 1) & 0xF;
                b = (x >> 2) | (x << 5);
                c = 11;
            }
                break;
            case 191:
            {
                var x = (bits >> 1) & 0x1F;
                b = (x >> 4) | (x << 4);
                c = 5;
            }
                break;
            
            default: throw new ArgumentOutOfRangeException(nameof(range), $"ASTC: illegal trit endpoint range {range}.");
        }

        var t = (trit * c) + b;
        t ^= a;
        return (a & 0x80) | (t >> 2);
    }

    private static int UnquantQuintEndpoint(int quint, int bits, int range)
    {
        var a = (bits & 1) != 0 ? 0x1FF : 0;
        int b, c;
        switch (range)
        {
            case 9:
                b = 0;
                c = 113;
                break;
            case 19:
            {
                var x = (bits >> 1) & 0x1;
                b = (x << 2) | (x << 3) | (x << 8);
                c = 54;
            }
                break;
            case 39:
            {
                var x = (bits >> 1) & 0x3;
                b = (x >> 1) | (x << 1) | (x << 7);
                c = 26;
            }
                break;
            case 79:
            {
                var x = (bits >> 1) & 0x7;
                b = (x >> 1) | (x << 6);
                c = 13;
            }
                break;
            case 159:
            {
                var x = (bits >> 1) & 0xF;
                b = (x >> 3) | (x << 5);
                c = 6;
            }
                break;
            
            default: throw new ArgumentOutOfRangeException(nameof(range), $"ASTC: illegal quint endpoint range {range}.");
        }

        var t = (quint * c) + b;
        t ^= a;
        return (a & 0x80) | (t >> 2);
    }

    // ------------------------------------------------------------------
    //  Weight reconstruction (C.2.17) -> [0,63] (caller applies the [0,64] fixup)
    // ------------------------------------------------------------------

    private static int UnquantTritWeight(int trit, int bits, int range)
    {
        if (range == 2)
        {
            return trit switch { 0 => 0, 1 => 32, _ => 63 };
        }

        var a = (bits & 1) != 0 ? 0x7F : 0;
        int b, c;
        switch (range)
        {
            case 5:
                c = 50;
                b = 0;
                break;
            case 11:
            {
                c = 23;
                b = (bits >> 1) & 1;
                b |= (b << 2) | (b << 6);
            }
                break;
            case 23:
            {
                c = 11;
                b = (bits >> 1) & 0x3;
                b |= b << 5;
            }
                break;
            
            default: throw new ArgumentOutOfRangeException(nameof(range), $"ASTC: illegal trit weight range {range}.");
        }

        var t = (trit * c) + b;
        t ^= a;
        return (a & 0x20) | (t >> 2);
    }

    private static int UnquantQuintWeight(int quint, int bits, int range)
    {
        if (range == 4)
        {
            return quint switch { 0 => 0, 1 => 16, 2 => 32, 3 => 47, _ => 63 };
        }

        var a = (bits & 1) != 0 ? 0x7F : 0;
        int b, c;
        switch (range)
        {
            case 9:
                c = 28;
                b = 0;
                break;
            case 19:
            {
                c = 13;
                b = (bits >> 1) & 0x1;
                b = (b << 1) | (b << 6);
            }
                break;
            
            default: throw new ArgumentOutOfRangeException(nameof(range), $"ASTC: illegal quint weight range {range}.");
        }

        var t = (quint * c) + b;
        t ^= a;
        return (a & 0x20) | (t >> 2);
    }

    /// <summary>Bit-replicates a <paramref name="srcBits"/>-wide value up to <paramref name="dstBits"/> bits.</summary>
    private static int Replicate(int value, int srcBits, int dstBits)
    {
        if (srcBits == 0)
        {
            return 0;
        }

        var result = value;
        var filled = srcBits;
        while (filled < dstBits)
        {
            var shift = Math.Min(srcBits, dstBits - filled);
            result = (result << shift) | (value >> (srcBits - shift));
            filled += shift;
        }

        return result;
    }
}
