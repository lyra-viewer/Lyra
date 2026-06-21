namespace Lyra.ManagedCodecs.Texture.Blocks.Astc;

/// <summary>
/// Decodes one ASTC LDR block to 8-bit RGBA. Ties the pieces together: block mode + weights
/// (<see cref="AstcWeights"/>), color-endpoint values (<see cref="AstcIntegerSequence"/> +
/// <see cref="AstcQuantization"/>), the per-partition color-endpoint modes, the seeded partition
/// pattern, and the final per-texel weighted interpolation (spec C.2.19, unorm8 decode path).
///
/// HDR endpoint modes are not handled (the LDR-only formats never use them); a block requesting one,
/// or any malformed encoding, returns <c>false</c> so the caller can emit an error color.
/// </summary>
internal static class AstcBlockDecoder
{
    // Color-endpoint quantization levels (max value), largest-first for the "best fit" search.
    private static readonly int[] EndpointLevels = [255, 191, 159, 127, 95, 79, 63, 47, 39, 31, 23, 19, 15, 11, 9, 7, 5];

    private static readonly int[] ExtraCemBits = [0, 2, 5, 8];

    /// <summary>Decodes a 2D block into <paramref name="dst"/> (blockW·blockH RGBA8). Returns false on error.</summary>
    public static bool TryDecode(ReadOnlySpan<byte> block, int blockW, int blockH, Span<byte> dst)
        => TryDecode(block, blockW, blockH, blockD: 1, dst);

    /// <summary>
    /// Decodes a block into <paramref name="dst"/> (blockW·blockH·blockD RGBA8, texels ordered X then Y
    /// then Z). <paramref name="blockD"/> of 1 is the 2D path; greater than 1 selects the 3D weight-grid
    /// and partition logic. Returns false on error.
    /// </summary>
    public static bool TryDecode(ReadOnlySpan<byte> block, int blockW, int blockH, int blockD, Span<byte> dst)
    {
        var is3d = blockD > 1;
        var mode = AstcWeights.DecodeMode(block, is3d);
        if (mode.Error)
        {
            return false;
        }

        var texels = blockW * blockH * blockD;

        if (mode.VoidExtent)
        {
            var r = (int)(AstcIntegerSequence.ReadBits(block, 64, 16) >> 8);
            var g = (int)(AstcIntegerSequence.ReadBits(block, 80, 16) >> 8);
            var bl = (int)(AstcIntegerSequence.ReadBits(block, 96, 16) >> 8);
            var al = (int)(AstcIntegerSequence.ReadBits(block, 112, 16) >> 8);
            for (var i = 0; i < texels; i++)
            {
                dst[(i * 4) + 0] = (byte)r;
                dst[(i * 4) + 1] = (byte)g;
                dst[(i * 4) + 2] = (byte)bl;
                dst[(i * 4) + 3] = (byte)al;
            }

            return true;
        }

        Span<int> weightsPrimary = stackalloc int[216]; // max 3D footprint 6x6x6
        Span<int> weightsSecondary = stackalloc int[216];
        AstcWeights.DecodeWeights(block, mode, blockW, blockH, blockD, weightsPrimary[..texels], weightsSecondary[..texels]);

        var numPartitions = 1 + (int)AstcIntegerSequence.ReadBits(block, 11, 2);
        var partitionId = numPartitions > 1 ? (int)AstcIntegerSequence.ReadBits(block, 13, 10) : 0;
        var numExtraCem = NumExtraCemBits(block, numPartitions);

        Span<int> cems = stackalloc int[4];
        var numColorValues = 0;
        for (var p = 0; p < numPartitions; p++)
        {
            cems[p] = EndpointMode(block, p, numPartitions, mode.WeightBits, numExtraCem);
            numColorValues += 2 * ((cems[p] >> 2) + 1);
        }

        if (numColorValues > 18)
            return false;

        var colorStartBit = numPartitions == 1 ? 17 : 29;
        var dualStart = 128 - mode.WeightBits - numExtraCem - (mode.DualPlane ? 2 : 0);
        var maxColorBits = dualStart - colorStartBit;

        var colorRange = BestColorRange(numColorValues, maxColorBits);
        if (colorRange == 0)
            return false;

        AstcIntegerSequence.CountsForRange(colorRange, out var ct, out var cq, out var cb);
        Span<int> colorValues = stackalloc int[18];
        AstcIntegerSequence.Decode(block, colorStartBit, numColorValues, ct, cq, cb, colorValues);
        for (var i = 0; i < numColorValues; i++) 
            colorValues[i] = AstcQuantization.UnquantizeEndpoint(colorValues[i], colorRange);

        Span<int> ep0 = stackalloc int[16]; // [partition*4 + channel]
        Span<int> ep1 = stackalloc int[16];
        var valueIndex = 0;
        for (var p = 0; p < numPartitions; p++)
        {
            var count = 2 * ((cems[p] >> 2) + 1);
            if (!DecodeEndpointPair(cems[p], colorValues.Slice(valueIndex, count), ep0.Slice(p * 4, 4), ep1.Slice(p * 4, 4)))
                return false; // HDR / unsupported endpoint mode

            valueIndex += count;
        }

        var dualChannel = mode.DualPlane ? (int)AstcIntegerSequence.ReadBits(block, dualStart, 2) : -1;

        for (var tz = 0; tz < blockD; tz++)
        for (var ty = 0; ty < blockH; ty++)
        for (var tx = 0; tx < blockW; tx++)
        {
            var texel = ((tz * blockH) + ty) * blockW + tx;
            var partition = PartitionForTexel(numPartitions, partitionId, is3d, tx, ty, tz, texels);
            var primary = weightsPrimary[texel];
            var secondary = mode.DualPlane ? weightsSecondary[texel] : 0;

            for (var c = 0; c < 4; c++)
            {
                var weight = c == dualChannel ? secondary : primary;
                dst[(texel * 4) + c] = (byte)Interpolate(ep0[(partition * 4) + c], ep1[(partition * 4) + c], weight);
            }
        }

        return true;
    }

    /// <summary>The unorm8 weighted blend of two endpoint components by a [0,64] weight (spec C.2.19).</summary>
    private static int Interpolate(int e0, int e1, int weight)
    {
        var c0 = (e0 << 8) | 0x80;
        var c1 = (e1 << 8) | 0x80;
        return (((c0 * (64 - weight) + (c1 * weight) + 32) >> 6) >> 8);
    }

    private static int BestColorRange(int numColorValues, int maxColorBits)
    {
        foreach (var level in EndpointLevels)
        {
            AstcIntegerSequence.CountsForRange(level, out var t, out var q, out var b);
            
            if (AstcIntegerSequence.BitCount(numColorValues, t, q, b) <= maxColorBits)
                return level;
        }

        return 0;
    }

    private static int NumExtraCemBits(ReadOnlySpan<byte> block, int numPartitions)
    {
        if (numPartitions == 1)
            return 0;

        var sharedCem = AstcIntegerSequence.ReadBits(block, 23, 2);
        return sharedCem == 0 ? 0 : ExtraCemBits[numPartitions - 1];
    }

    /// <summary>The color-endpoint mode for one partition (spec C.2.11 / Figure C.4).</summary>
    private static int EndpointMode(ReadOnlySpan<byte> block, int partition, int numPartitions, int weightBits, int numExtraCem)
    {
        if (numPartitions == 1)
            return (int)AstcIntegerSequence.ReadBits(block, 13, 4);

        if (numExtraCem == 0)
            return (int)AstcIntegerSequence.ReadBits(block, 25, 4); // shared CEM

        var cem = (int)AstcIntegerSequence.ReadBits(block, 23, 6);
        var baseCem = ((cem & 3) - 1) * 4;
        cem >>= 2;

        var extraStart = 128 - numExtraCem - weightBits;
        var extra = (int)AstcIntegerSequence.ReadBits(block, extraStart, numExtraCem);
        cem |= extra << 4;

        var c = 0;
        for (var i = 0; i < numPartitions; i++)
        {
            if (i == partition) 
                c = cem & 1;

            cem >>= 1;
        }

        var m = 0;
        for (var i = 0; i < numPartitions; i++)
        {
            if (i == partition) 
                m = cem & 3;

            cem >>= 2;
        }

        return baseCem + (4 * c) + m;
    }

    // ------------------------------------------------------------------
    //  Endpoint colour modes (LDR subset). v holds the unquantized colour values.
    // ------------------------------------------------------------------

    private static bool DecodeEndpointPair(int mode, ReadOnlySpan<int> v, Span<int> ep0, Span<int> ep1)
    {
        switch (mode)
        {
            case 0: // LDR luminance, direct
                SetRgb(ep0, v[0], v[0], v[0], 255);
                SetRgb(ep1, v[1], v[1], v[1], 255);
                break;

            case 1: // LDR luminance, base+offset
            {
                var l0 = (v[0] >> 2) | (v[1] & 0xC0);
                var l1 = Math.Min(l0 + (v[1] & 0x3F), 0xFF);
                SetRgb(ep0, l0, l0, l0, 255);
                SetRgb(ep1, l1, l1, l1, 255);
                break;
            }

            case 4: // LDR luminance+alpha, direct
                SetRgb(ep0, v[0], v[0], v[0], v[2]);
                SetRgb(ep1, v[1], v[1], v[1], v[3]);
                break;

            case 5: // LDR luminance+alpha, base+offset
            {
                int v0 = v[0], v1 = v[1], v2 = v[2], v3 = v[3];
                BitTransferSigned(ref v1, ref v0);
                BitTransferSigned(ref v3, ref v2);
                SetRgb(ep0, v0, v0, v0, v2);
                SetRgb(ep1, v0 + v1, v0 + v1, v0 + v1, v2 + v3);
                break;
            }

            case 6: // LDR RGB, base+scale
                SetRgb(ep0, (v[0] * v[3]) >> 8, (v[1] * v[3]) >> 8, (v[2] * v[3]) >> 8, 255);
                SetRgb(ep1, v[0], v[1], v[2], 255);
                break;

            case 8: // LDR RGB, direct
                if (v[1] + v[3] + v[5] >= v[0] + v[2] + v[4])
                {
                    SetRgb(ep0, v[0], v[2], v[4], 255);
                    SetRgb(ep1, v[1], v[3], v[5], 255);
                }
                else
                {
                    BlueContract(ep0, v[1], v[3], v[5], 255);
                    BlueContract(ep1, v[0], v[2], v[4], 255);
                }

                break;

            case 9: // LDR RGB, base+offset
            {
                int v0 = v[0], v1 = v[1], v2 = v[2], v3 = v[3], v4 = v[4], v5 = v[5];
                BitTransferSigned(ref v1, ref v0);
                BitTransferSigned(ref v3, ref v2);
                BitTransferSigned(ref v5, ref v4);
                if (v1 + v3 + v5 >= 0)
                {
                    SetRgb(ep0, v0, v2, v4, 255);
                    SetRgb(ep1, v0 + v1, v2 + v3, v4 + v5, 255);
                }
                else
                {
                    BlueContract(ep0, v0 + v1, v2 + v3, v4 + v5, 255);
                    BlueContract(ep1, v0, v2, v4, 255);
                }

                break;
            }

            case 10: // LDR RGB, base+scale with two alphas
                SetRgb(ep0, (v[0] * v[3]) >> 8, (v[1] * v[3]) >> 8, (v[2] * v[3]) >> 8, v[4]);
                SetRgb(ep1, v[0], v[1], v[2], v[5]);
                break;

            case 12: // LDR RGBA, direct
                if (v[1] + v[3] + v[5] >= v[0] + v[2] + v[4])
                {
                    SetRgb(ep0, v[0], v[2], v[4], v[6]);
                    SetRgb(ep1, v[1], v[3], v[5], v[7]);
                }
                else
                {
                    BlueContract(ep0, v[1], v[3], v[5], v[7]);
                    BlueContract(ep1, v[0], v[2], v[4], v[6]);
                }

                break;

            case 13: // LDR RGBA, base+offset
            {
                int v0 = v[0], v1 = v[1], v2 = v[2], v3 = v[3], v4 = v[4], v5 = v[5], v6 = v[6], v7 = v[7];
                BitTransferSigned(ref v1, ref v0);
                BitTransferSigned(ref v3, ref v2);
                BitTransferSigned(ref v5, ref v4);
                BitTransferSigned(ref v7, ref v6);
                if (v1 + v3 + v5 >= 0)
                {
                    SetRgb(ep0, v0, v2, v4, v6);
                    SetRgb(ep1, v0 + v1, v2 + v3, v4 + v5, v6 + v7);
                }
                else
                {
                    BlueContract(ep0, v0 + v1, v2 + v3, v4 + v5, v6 + v7);
                    BlueContract(ep1, v0, v2, v4, v6);
                }

                break;
            }

            default:
                return false; // HDR modes 2,3,7,11,14,15 — not supported
        }

        Clamp(ep0);
        Clamp(ep1);
        return true;
    }

    private static void SetRgb(Span<int> ep, int r, int g, int b, int a)
    {
        ep[0] = r;
        ep[1] = g;
        ep[2] = b;
        ep[3] = a;
    }

    private static void BlueContract(Span<int> ep, int r, int g, int b, int a)
    {
        ep[0] = (r + b) >> 1;
        ep[1] = (g + b) >> 1;
        ep[2] = b;
        ep[3] = a;
    }

    private static void BitTransferSigned(ref int a, ref int b)
    {
        b >>= 1;
        b |= a & 0x80;
        a >>= 1;
        a &= 0x3F;
        
        if ((a & 0x20) != 0) 
            a -= 0x40; // sign-extend 6 bits
    }

    private static void Clamp(Span<int> ep)
    {
        for (var i = 0; i < 4; i++) 
            ep[i] = ep[i] < 0 ? 0 : ep[i] > 255 ? 255 : ep[i];
    }

    /// <summary>Which partition a texel belongs to: a single-partition block is all partition 0, otherwise
    /// the seeded pattern is evaluated by the 2D or 3D selector as the footprint requires.</summary>
    private static int PartitionForTexel(int numPartitions, int partitionId, bool is3d, int tx, int ty, int tz, int numTexels)
    {
        if (numPartitions == 1)
            return 0;

        return is3d
            ? SelectPartition3D(partitionId, tx, ty, tz, numPartitions, numTexels)
            : SelectPartition(partitionId, tx, ty, numPartitions, numTexels);
    }

    /// <summary>The seeded ASTC 2D partition pattern (spec C.2.21): which partition a texel belongs to.</summary>
    private static int SelectPartition(int seed, int x, int y, int partitionCount, int numTexels)
    {
        if (numTexels < 31)
        {
            x <<= 1;
            y <<= 1;
        }

        Span<int> seeds = stackalloc int[12];
        var rnum = PrepareSeeds(seed, partitionCount, seeds);

        var a = (seeds[0] * x) + (seeds[1] * y) + (int)(rnum >> 14);
        var b = (seeds[2] * x) + (seeds[3] * y) + (int)(rnum >> 10);
        var c = (seeds[4] * x) + (seeds[5] * y) + (int)(rnum >> 6);
        var d = (seeds[6] * x) + (seeds[7] * y) + (int)(rnum >> 2);

        return SelectFromScores(a, b, c, d, partitionCount);
    }

    /// <summary>The 3D variant (spec C.2.21): the seed hash is identical; the Z axis adds three more seeds.</summary>
    private static int SelectPartition3D(int seed, int x, int y, int z, int partitionCount, int numTexels)
    {
        if (numTexels < 32)
        {
            x <<= 1;
            y <<= 1;
            z <<= 1;
        }

        Span<int> seeds = stackalloc int[12];
        var rnum = PrepareSeeds(seed, partitionCount, seeds);

        var a = (seeds[0] * x) + (seeds[1] * y) + (seeds[10] * z) + (int)(rnum >> 14);
        var b = (seeds[2] * x) + (seeds[3] * y) + (seeds[11] * z) + (int)(rnum >> 10);
        var c = (seeds[4] * x) + (seeds[5] * y) + (seeds[8] * z) + (int)(rnum >> 6);
        var d = (seeds[6] * x) + (seeds[7] * y) + (seeds[9] * z) + (int)(rnum >> 2);

        return SelectFromScores(a, b, c, d, partitionCount);
    }

    /// <summary>The shared seed hash (hash52) and per-seed shift prep used by both partition selectors.</summary>
    private static uint PrepareSeeds(int seed, int partitionCount, Span<int> seeds)
    {
        seed += (partitionCount - 1) * 1024;

        var rnum = (uint)seed;
        rnum ^= rnum >> 15;
        rnum -= rnum << 17;
        rnum += rnum << 7;
        rnum += rnum << 4;
        rnum ^= rnum >> 5;
        rnum += rnum << 16;
        rnum ^= rnum >> 7;
        rnum ^= rnum >> 3;
        rnum ^= rnum << 6;
        rnum ^= rnum >> 17;

        for (var i = 0; i < 8; i++) 
            seeds[i] = (int)((rnum >> (i * 4)) & 0xF);

        seeds[8] = (int)((rnum >> 18) & 0xF);
        seeds[9] = (int)((rnum >> 22) & 0xF);
        seeds[10] = (int)((rnum >> 26) & 0xF);
        seeds[11] = (int)(((rnum >> 30) | (rnum << 2)) & 0xF);
        
        for (var i = 0; i < 12; i++) 
            seeds[i] *= seeds[i];

        int sh1, sh2;
        if ((seed & 1) != 0)
        {
            sh1 = (seed & 2) != 0 ? 4 : 5;
            sh2 = partitionCount == 3 ? 6 : 5;
        }
        else
        {
            sh1 = partitionCount == 3 ? 6 : 5;
            sh2 = (seed & 2) != 0 ? 4 : 5;
        }

        var sh3 = (seed & 0x10) != 0 ? sh1 : sh2;

        seeds[0] >>= sh1; seeds[1] >>= sh2; seeds[2] >>= sh1; seeds[3] >>= sh2;
        seeds[4] >>= sh1; seeds[5] >>= sh2; seeds[6] >>= sh1; seeds[7] >>= sh2;
        seeds[8] >>= sh3; seeds[9] >>= sh3; seeds[10] >>= sh3; seeds[11] >>= sh3;

        return rnum;
    }

    /// <summary>Picks the highest-scoring partition (ties to the lowest index), zeroing unused axes.</summary>
    private static int SelectFromScores(int a, int b, int c, int d, int partitionCount)
    {
        a &= 0x3F;
        b &= 0x3F;
        c &= 0x3F;
        d &= 0x3F;

        if (partitionCount <= 3) 
            d = 0;

        if (partitionCount <= 2) 
            c = 0;

        if (a >= b && a >= c && a >= d)
            return 0;

        if (b >= c && b >= d)
            return 1;

        return c >= d ? 2 : 3;
    }
}
