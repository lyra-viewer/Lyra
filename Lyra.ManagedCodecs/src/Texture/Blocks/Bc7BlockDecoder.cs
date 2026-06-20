namespace Lyra.ManagedCodecs.Texture.Blocks;

/// <summary>
/// Decodes a 16-byte BC7 (BPTC) block to 16 RGBA8 pixels (row-major). BC7 packs eight different
/// block modes varying in subset count, partitioning, endpoint precision, P-bits, rotation and dual
/// index sets; this mirrors the reference algorithm and partition tables from the public-domain
/// bcdec.h (see <see cref="Bc7Tables"/>).
/// </summary>
internal static class Bc7BlockDecoder
{
    // Endpoint component precision per mode: [0] = RGB bits, [1] = alpha bits.
    private static readonly int[] RgbBits = [4, 6, 5, 7, 5, 7, 7, 5];
    private static readonly int[] AlphaBits = [0, 0, 0, 0, 6, 8, 7, 5];

    // Bitmask of modes that carry P-bits (modes 0,1,3,6,7).
    private const int ModesWithPBits = 0b1100_1011;

    private static readonly int[] Weight2 = [0, 21, 43, 64];
    private static readonly int[] Weight3 = [0, 9, 18, 27, 37, 46, 55, 64];
    private static readonly int[] Weight4 = [0, 4, 9, 13, 17, 21, 26, 30, 34, 38, 43, 47, 51, 55, 60, 64];

    public static void DecodeBc7(ReadOnlySpan<byte> block, Span<byte> dst)
    {
        var bits = new BitReader(block);

        // Mode is a unary prefix: the count of leading zero bits before the first 1.
        var mode = 0;
        while (mode < 8 && bits.ReadBit() == 0)
        {
            mode++;
        }

        if (mode >= 8)
        {
            dst[..64].Clear(); // reserved mode -> transparent black
            return;
        }

        var numPartitions = mode is 0 or 2 ? 3 : mode is 1 or 3 or 7 ? 2 : 1;
        var partition = 0;
        var rotation = 0;
        var indexSelectionBit = 0;

        if (numPartitions > 1)
        {
            partition = bits.ReadBits(mode == 0 ? 4 : 6);
        }

        if (mode is 4 or 5)
        {
            rotation = bits.ReadBits(2);
            if (mode == 4)
            {
                indexSelectionBit = bits.ReadBit();
            }
        }

        var numEndpoints = numPartitions * 2;

        // Up to 6 endpoints (3 subsets * 2), RGBA each, indexed [(endpoint * 4) + channel].
        // stackalloc keeps this off the heap - it is decoded once per 4x4 block.
        Span<int> endpoints = stackalloc int[6 * 4];
        endpoints.Clear();

        var rgbBits = RgbBits[mode];
        var alphaBits = AlphaBits[mode];

        // Endpoints are stored component-major: all R, all G, all B, then all A.
        for (var c = 0; c < 3; c++)
        {
            for (var e = 0; e < numEndpoints; e++)
            {
                endpoints[(e * 4) + c] = bits.ReadBits(rgbBits);
            }
        }

        if (alphaBits > 0)
        {
            for (var e = 0; e < numEndpoints; e++)
            {
                endpoints[(e * 4) + 3] = bits.ReadBits(alphaBits);
            }
        }

        var hasPBits = (ModesWithPBits & (1 << mode)) != 0;
        if (hasPBits)
        {
            for (var e = 0; e < numEndpoints; e++)
            {
                for (var c = 0; c < 4; c++)
                {
                    endpoints[(e * 4) + c] <<= 1;
                }
            }

            if (mode == 1)
            {
                // Two P-bits shared by the two endpoints of each subset.
                var p0 = bits.ReadBit();
                var p1 = bits.ReadBit();
                for (var c = 0; c < 3; c++)
                {
                    endpoints[(0 * 4) + c] |= p0;
                    endpoints[(1 * 4) + c] |= p0;
                    endpoints[(2 * 4) + c] |= p1;
                    endpoints[(3 * 4) + c] |= p1;
                }
            }
            else
            {
                // One P-bit per endpoint, applied to every component.
                for (var e = 0; e < numEndpoints; e++)
                {
                    var p = bits.ReadBit();
                    for (var c = 0; c < 4; c++)
                    {
                        endpoints[(e * 4) + c] |= p;
                    }
                }
            }
        }

        // Expand each component to 8 bits by left-justifying its MSB and replicating into the low bits.
        var pBit = hasPBits ? 1 : 0;
        var colorPrecision = rgbBits + pBit;
        var alphaPrecision = alphaBits + pBit;

        for (var e = 0; e < numEndpoints; e++)
        {
            for (var c = 0; c < 3; c++)
            {
                var v = endpoints[(e * 4) + c] << (8 - colorPrecision);
                endpoints[(e * 4) + c] = v | (v >> colorPrecision);
            }

            if (alphaBits > 0)
            {
                var v = endpoints[(e * 4) + 3] << (8 - alphaPrecision);
                endpoints[(e * 4) + 3] = v | (v >> alphaPrecision);
            }
            else
            {
                endpoints[(e * 4) + 3] = 0xFF;
            }
        }

        var colorIndexBits = mode is 0 or 1 ? 3 : mode == 6 ? 4 : 2;
        var alphaIndexBits = mode == 4 ? 3 : mode == 5 ? 2 : 0;
        var colorWeights = WeightsFor(colorIndexBits);
        var alphaWeights = WeightsFor(alphaIndexBits == 0 ? 2 : alphaIndexBits);

        var partitionTable = numPartitions == 2 ? Bc7Tables.Partition2 : Bc7Tables.Partition3;

        // Pass 1: read all primary indices (anchors use one fewer bit).
        Span<byte> primary = stackalloc byte[16];
        for (var t = 0; t < 16; t++)
        {
            var set = PartitionSet(numPartitions, partitionTable, partition, t);
            var ib = colorIndexBits - ((set & 0x80) != 0 ? 1 : 0);
            primary[t] = (byte)bits.ReadBits(ib);
        }

        // Pass 2: read secondary indices (if any) and write interpolated, rotated pixels.
        for (var t = 0; t < 16; t++)
        {
            var set = PartitionSet(numPartitions, partitionTable, partition, t) & 0x03;
            var e0 = set * 2;
            var e1 = (set * 2) + 1;
            var index = primary[t];

            var c0 = e0 * 4;
            var c1 = e1 * 4;

            int r, g, b, a;
            if (alphaIndexBits == 0)
            {
                r = Interpolate(endpoints[c0 + 0], endpoints[c1 + 0], colorWeights, index);
                g = Interpolate(endpoints[c0 + 1], endpoints[c1 + 1], colorWeights, index);
                b = Interpolate(endpoints[c0 + 2], endpoints[c1 + 2], colorWeights, index);
                a = Interpolate(endpoints[c0 + 3], endpoints[c1 + 3], colorWeights, index);
            }
            else
            {
                var index2 = bits.ReadBits(t == 0 ? alphaIndexBits - 1 : alphaIndexBits);
                if (indexSelectionBit == 0)
                {
                    r = Interpolate(endpoints[c0 + 0], endpoints[c1 + 0], colorWeights, index);
                    g = Interpolate(endpoints[c0 + 1], endpoints[c1 + 1], colorWeights, index);
                    b = Interpolate(endpoints[c0 + 2], endpoints[c1 + 2], colorWeights, index);
                    a = Interpolate(endpoints[c0 + 3], endpoints[c1 + 3], alphaWeights, index2);
                }
                else
                {
                    r = Interpolate(endpoints[c0 + 0], endpoints[c1 + 0], alphaWeights, index2);
                    g = Interpolate(endpoints[c0 + 1], endpoints[c1 + 1], alphaWeights, index2);
                    b = Interpolate(endpoints[c0 + 2], endpoints[c1 + 2], alphaWeights, index2);
                    a = Interpolate(endpoints[c0 + 3], endpoints[c1 + 3], colorWeights, index);
                }
            }

            switch (rotation)
            {
                case 1: (a, r) = (r, a); break;
                case 2: (a, g) = (g, a); break;
                case 3: (a, b) = (b, a); break;
            }

            var o = t * 4;
            dst[o + 0] = (byte)r;
            dst[o + 1] = (byte)g;
            dst[o + 2] = (byte)b;
            dst[o + 3] = (byte)a;
        }
    }

    private static int PartitionSet(int numPartitions, byte[] table, int partition, int texel)
        => numPartitions == 1 ? (texel == 0 ? 128 : 0) : table[(partition * 16) + texel];

    private static int[] WeightsFor(int indexBits) => indexBits switch
    {
        2 => Weight2,
        3 => Weight3,
        _ => Weight4,
    };

    private static int Interpolate(int a, int b, int[] weights, int index)
        => ((a * (64 - weights[index])) + (b * weights[index]) + 32) >> 6;

    /// <summary>Reads the 128-bit block LSB-first (byte 0, bit 0 is the first bit consumed).</summary>
    private ref struct BitReader(ReadOnlySpan<byte> block)
    {
        private readonly ReadOnlySpan<byte> _block = block;
        private int _pos = 0;

        public int ReadBit()
        {
            var bit = (_block[_pos >> 3] >> (_pos & 7)) & 1;
            _pos++;
            return bit;
        }

        public int ReadBits(int count)
        {
            var value = 0;
            for (var i = 0; i < count; i++)
            {
                value |= ReadBit() << i;
            }

            return value;
        }
    }
}