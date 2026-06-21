namespace Lyra.ManagedCodecs.Texture.Blocks.Astc;

/// <summary>
/// ASTC Bounded Integer Sequence Encoding (BISE), per spec section C.2.12 - the packing every ASTC
/// weight and endpoint stream uses. Values live in a range of the form 2^b, 3·2^b (a "trit" plus b
/// bits) or 5·2^b (a "quint" plus b bits); trits/quints are interleaved five-to-a-byte / three-to-7-bits
/// and recovered through the lookup tables below.
///
/// The trit (256×5) and quint (128×3) decode tables are taken verbatim from Google's Apache-2.0
/// astc-codec; the decode procedure mirrors its <c>DecodeISEBlock</c>. This is the foundation the
/// weight and endpoint decoders build on.
/// </summary>
internal static class AstcIntegerSequence
{
    // How many interleaved (trit/quint) bits follow each value within an ISE block.
    private static readonly int[] TritInterleave =  [2, 2, 1, 2, 1]; // 5 trits -> 8 bits
    private static readonly int[] QuintInterleave = [3, 2, 2];       // 3 quints -> 7 bits

    /// <summary>
    /// Decodes <paramref name="count"/> values from the integer sequence starting at
    /// <paramref name="startBit"/> (LSB-first) into <paramref name="dst"/>. The range is described by
    /// (<paramref name="trits"/>, <paramref name="quints"/>, <paramref name="bits"/>): at most one of
    /// trits/quints is set, with <paramref name="bits"/> low bits per value.
    /// </summary>
    public static void Decode(ReadOnlySpan<byte> block, int startBit, int count, int trits, int quints, int bits, Span<int> dst)
    {
        if (trits == 0 && quints == 0)
        {
            for (var i = 0; i < count; i++) 
                dst[i] = (int)ReadBits(block, startBit + (i * bits), bits);

            return;
        }

        var valsPerBlock = trits != 0 ? 5 : 3;
        var interleave = trits != 0 ? TritInterleave : QuintInterleave;
        var interleaveBits = trits != 0 ? 8 : 7;
        var blockBits = interleaveBits + (valsPerBlock * bits);

        var totalBits = BitCount(count, trits, quints, bits);
        var pos = startBit;
        var bitsLeft = totalBits;
        var produced = 0;

        Span<int> m = stackalloc int[5];
        while (bitsLeft > 0 && produced < count)
        {
            var take = Math.Min(bitsLeft, blockBits);
            var bb = ReadBits64(block, pos, take); // high bits read as zero past `take`, matching the spec
            pos += take;
            bitsLeft -= blockBits;

            // Split each value's low bits (m) from the interleaved trit/quint bits, reassembling the
            // packed trit/quint index in declaration order.
            var cursor = 0;
            var packed = 0;
            var packedBits = 0;
            for (var i = 0; i < valsPerBlock; i++)
            {
                m[i] = bits == 0 ? 0 : (int)((bb >> cursor) & ((1UL << bits) - 1));
                cursor += bits;

                var ib = interleave[i];
                var e = (int)((bb >> cursor) & ((1UL << ib) - 1));
                cursor += ib;
                packed |= e << packedBits;
                packedBits += ib;
            }

            for (var i = 0; i < valsPerBlock && produced < count; i++)
            {
                var digit = trits != 0 ? TritTable[(packed * 5) + i] : QuintTable[(packed * 3) + i];
                dst[produced++] = (digit << bits) | m[i];
            }
        }
    }

    /// <summary>Total bits a sequence of <paramref name="count"/> values occupies (spec C.2.22).</summary>
    public static int BitCount(int count, int trits, int quints, int bits)
    {
        var tritBits = (((count * 8 * trits) + 4) / 5);
        var quintBits = (((count * 7 * quints) + 2) / 3);
        return tritBits + quintBits + (count * bits);
    }

    /// <summary>
    /// Resolves a quantization range (max representable value, i.e. levels-1) into its
    /// trit/quint/bit composition. ASTC quant levels are always exactly 2^b, 3·2^b or 5·2^b.
    /// </summary>
    public static void CountsForRange(int maxValue, out int trits, out int quints, out int bits)
    {
        var levels = maxValue + 1;
        for (var b = 0; b < 16; b++)
        {
            var pow = 1 << b;
            if (levels == pow)
            {
                trits = 0;
                quints = 0;
                bits = b;
                return;
            }

            if (levels == 3 * pow)
            {
                trits = 1;
                quints = 0;
                bits = b;
                return;
            }

            if (levels == 5 * pow)
            {
                trits = 0;
                quints = 1;
                bits = b;
                return;
            }
        }

        throw new ArgumentOutOfRangeException(nameof(maxValue), $"ASTC: {maxValue} is not a valid quantization range.");
    }

    /// <summary>Reads <paramref name="count"/> bits (≤32) starting at <paramref name="start"/>, LSB-first.</summary>
    public static uint ReadBits(ReadOnlySpan<byte> block, int start, int count) 
        => (uint)ReadBits64(block, start, count);

    private static ulong ReadBits64(ReadOnlySpan<byte> block, int start, int count)
    {
        ulong result = 0;
        for (var i = 0; i < count; i++)
        {
            var bit = start + i;
            var b = (block[bit >> 3] >> (bit & 7)) & 1;
            result |= (ulong)b << i;
        }

        return result;
    }

    // ------------------------------------------------------------------
    //  Trit / quint decode tables (verbatim from google/astc-codec, Apache-2.0).
    //  TritTable[packed*5 + i] = the i-th trit (0..2) of the 8-bit packed value.
    //  QuintTable[packed*3 + i] = the i-th quint (0..4) of the 7-bit packed value.
    // ------------------------------------------------------------------

    private static readonly byte[] TritTable =
    [
        0, 0, 0, 0, 0, 1, 0, 0, 0, 0, 2, 0, 0, 0, 0, 0, 0, 2, 0, 0, 0, 1, 0, 0, 0, 1, 1, 0, 0, 0, 2, 1, 0, 0, 0, 1, 0, 2, 0, 0,
        0, 2, 0, 0, 0, 1, 2, 0, 0, 0, 2, 2, 0, 0, 0, 2, 0, 2, 0, 0, 0, 2, 2, 0, 0, 1, 2, 2, 0, 0, 2, 2, 2, 0, 0, 2, 0, 2, 0, 0,
        0, 0, 1, 0, 0, 1, 0, 1, 0, 0, 2, 0, 1, 0, 0, 0, 1, 2, 0, 0, 0, 1, 1, 0, 0, 1, 1, 1, 0, 0, 2, 1, 1, 0, 0, 1, 1, 2, 0, 0,
        0, 2, 1, 0, 0, 1, 2, 1, 0, 0, 2, 2, 1, 0, 0, 2, 1, 2, 0, 0, 0, 0, 0, 2, 2, 1, 0, 0, 2, 2, 2, 0, 0, 2, 2, 0, 0, 2, 2, 2,
        0, 0, 0, 1, 0, 1, 0, 0, 1, 0, 2, 0, 0, 1, 0, 0, 0, 2, 1, 0, 0, 1, 0, 1, 0, 1, 1, 0, 1, 0, 2, 1, 0, 1, 0, 1, 0, 2, 1, 0,
        0, 2, 0, 1, 0, 1, 2, 0, 1, 0, 2, 2, 0, 1, 0, 2, 0, 2, 1, 0, 0, 2, 2, 1, 0, 1, 2, 2, 1, 0, 2, 2, 2, 1, 0, 2, 0, 2, 1, 0,
        0, 0, 1, 1, 0, 1, 0, 1, 1, 0, 2, 0, 1, 1, 0, 0, 1, 2, 1, 0, 0, 1, 1, 1, 0, 1, 1, 1, 1, 0, 2, 1, 1, 1, 0, 1, 1, 2, 1, 0,
        0, 2, 1, 1, 0, 1, 2, 1, 1, 0, 2, 2, 1, 1, 0, 2, 1, 2, 1, 0, 0, 1, 0, 2, 2, 1, 1, 0, 2, 2, 2, 1, 0, 2, 2, 1, 0, 2, 2, 2,
        0, 0, 0, 2, 0, 1, 0, 0, 2, 0, 2, 0, 0, 2, 0, 0, 0, 2, 2, 0, 0, 1, 0, 2, 0, 1, 1, 0, 2, 0, 2, 1, 0, 2, 0, 1, 0, 2, 2, 0,
        0, 2, 0, 2, 0, 1, 2, 0, 2, 0, 2, 2, 0, 2, 0, 2, 0, 2, 2, 0, 0, 2, 2, 2, 0, 1, 2, 2, 2, 0, 2, 2, 2, 2, 0, 2, 0, 2, 2, 0,
        0, 0, 1, 2, 0, 1, 0, 1, 2, 0, 2, 0, 1, 2, 0, 0, 1, 2, 2, 0, 0, 1, 1, 2, 0, 1, 1, 1, 2, 0, 2, 1, 1, 2, 0, 1, 1, 2, 2, 0,
        0, 2, 1, 2, 0, 1, 2, 1, 2, 0, 2, 2, 1, 2, 0, 2, 1, 2, 2, 0, 0, 2, 0, 2, 2, 1, 2, 0, 2, 2, 2, 2, 0, 2, 2, 2, 0, 2, 2, 2,
        0, 0, 0, 0, 2, 1, 0, 0, 0, 2, 2, 0, 0, 0, 2, 0, 0, 2, 0, 2, 0, 1, 0, 0, 2, 1, 1, 0, 0, 2, 2, 1, 0, 0, 2, 1, 0, 2, 0, 2,
        0, 2, 0, 0, 2, 1, 2, 0, 0, 2, 2, 2, 0, 0, 2, 2, 0, 2, 0, 2, 0, 2, 2, 0, 2, 1, 2, 2, 0, 2, 2, 2, 2, 0, 2, 2, 0, 2, 0, 2,
        0, 0, 1, 0, 2, 1, 0, 1, 0, 2, 2, 0, 1, 0, 2, 0, 1, 2, 0, 2, 0, 1, 1, 0, 2, 1, 1, 1, 0, 2, 2, 1, 1, 0, 2, 1, 1, 2, 0, 2,
        0, 2, 1, 0, 2, 1, 2, 1, 0, 2, 2, 2, 1, 0, 2, 2, 1, 2, 0, 2, 0, 2, 2, 2, 2, 1, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 0, 2, 2, 2,
        0, 0, 0, 0, 1, 1, 0, 0, 0, 1, 2, 0, 0, 0, 1, 0, 0, 2, 0, 1, 0, 1, 0, 0, 1, 1, 1, 0, 0, 1, 2, 1, 0, 0, 1, 1, 0, 2, 0, 1,
        0, 2, 0, 0, 1, 1, 2, 0, 0, 1, 2, 2, 0, 0, 1, 2, 0, 2, 0, 1, 0, 2, 2, 0, 1, 1, 2, 2, 0, 1, 2, 2, 2, 0, 1, 2, 0, 2, 0, 1,
        0, 0, 1, 0, 1, 1, 0, 1, 0, 1, 2, 0, 1, 0, 1, 0, 1, 2, 0, 1, 0, 1, 1, 0, 1, 1, 1, 1, 0, 1, 2, 1, 1, 0, 1, 1, 1, 2, 0, 1,
        0, 2, 1, 0, 1, 1, 2, 1, 0, 1, 2, 2, 1, 0, 1, 2, 1, 2, 0, 1, 0, 0, 1, 2, 2, 1, 0, 1, 2, 2, 2, 0, 1, 2, 2, 0, 1, 2, 2, 2,
        0, 0, 0, 1, 1, 1, 0, 0, 1, 1, 2, 0, 0, 1, 1, 0, 0, 2, 1, 1, 0, 1, 0, 1, 1, 1, 1, 0, 1, 1, 2, 1, 0, 1, 1, 1, 0, 2, 1, 1,
        0, 2, 0, 1, 1, 1, 2, 0, 1, 1, 2, 2, 0, 1, 1, 2, 0, 2, 1, 1, 0, 2, 2, 1, 1, 1, 2, 2, 1, 1, 2, 2, 2, 1, 1, 2, 0, 2, 1, 1,
        0, 0, 1, 1, 1, 1, 0, 1, 1, 1, 2, 0, 1, 1, 1, 0, 1, 2, 1, 1, 0, 1, 1, 1, 1, 1, 1, 1, 1, 1, 2, 1, 1, 1, 1, 1, 1, 2, 1, 1,
        0, 2, 1, 1, 1, 1, 2, 1, 1, 1, 2, 2, 1, 1, 1, 2, 1, 2, 1, 1, 0, 1, 1, 2, 2, 1, 1, 1, 2, 2, 2, 1, 1, 2, 2, 1, 1, 2, 2, 2,
        0, 0, 0, 2, 1, 1, 0, 0, 2, 1, 2, 0, 0, 2, 1, 0, 0, 2, 2, 1, 0, 1, 0, 2, 1, 1, 1, 0, 2, 1, 2, 1, 0, 2, 1, 1, 0, 2, 2, 1,
        0, 2, 0, 2, 1, 1, 2, 0, 2, 1, 2, 2, 0, 2, 1, 2, 0, 2, 2, 1, 0, 2, 2, 2, 1, 1, 2, 2, 2, 1, 2, 2, 2, 2, 1, 2, 0, 2, 2, 1,
        0, 0, 1, 2, 1, 1, 0, 1, 2, 1, 2, 0, 1, 2, 1, 0, 1, 2, 2, 1, 0, 1, 1, 2, 1, 1, 1, 1, 2, 1, 2, 1, 1, 2, 1, 1, 1, 2, 2, 1,
        0, 2, 1, 2, 1, 1, 2, 1, 2, 1, 2, 2, 1, 2, 1, 2, 1, 2, 2, 1, 0, 2, 1, 2, 2, 1, 2, 1, 2, 2, 2, 2, 1, 2, 2, 2, 1, 2, 2, 2,
        0, 0, 0, 1, 2, 1, 0, 0, 1, 2, 2, 0, 0, 1, 2, 0, 0, 2, 1, 2, 0, 1, 0, 1, 2, 1, 1, 0, 1, 2, 2, 1, 0, 1, 2, 1, 0, 2, 1, 2,
        0, 2, 0, 1, 2, 1, 2, 0, 1, 2, 2, 2, 0, 1, 2, 2, 0, 2, 1, 2, 0, 2, 2, 1, 2, 1, 2, 2, 1, 2, 2, 2, 2, 1, 2, 2, 0, 2, 1, 2,
        0, 0, 1, 1, 2, 1, 0, 1, 1, 2, 2, 0, 1, 1, 2, 0, 1, 2, 1, 2, 0, 1, 1, 1, 2, 1, 1, 1, 1, 2, 2, 1, 1, 1, 2, 1, 1, 2, 1, 2,
        0, 2, 1, 1, 2, 1, 2, 1, 1, 2, 2, 2, 1, 1, 2, 2, 1, 2, 1, 2, 0, 2, 2, 2, 2, 1, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 1, 2, 2, 2,
    ];

    private static readonly byte[] QuintTable =
    [
        0, 0, 0, 1, 0, 0, 2, 0, 0, 3, 0, 0, 4, 0, 0, 0, 4, 0, 4, 4, 0, 4, 4, 4, 0, 1, 0, 1, 1, 0,
        2, 1, 0, 3, 1, 0, 4, 1, 0, 1, 4, 0, 4, 4, 1, 4, 4, 4, 0, 2, 0, 1, 2, 0, 2, 2, 0, 3, 2, 0,
        4, 2, 0, 2, 4, 0, 4, 4, 2, 4, 4, 4, 0, 3, 0, 1, 3, 0, 2, 3, 0, 3, 3, 0, 4, 3, 0, 3, 4, 0,
        4, 4, 3, 4, 4, 4, 0, 0, 1, 1, 0, 1, 2, 0, 1, 3, 0, 1, 4, 0, 1, 0, 4, 1, 4, 0, 4, 0, 4, 4,
        0, 1, 1, 1, 1, 1, 2, 1, 1, 3, 1, 1, 4, 1, 1, 1, 4, 1, 4, 1, 4, 1, 4, 4, 0, 2, 1, 1, 2, 1,
        2, 2, 1, 3, 2, 1, 4, 2, 1, 2, 4, 1, 4, 2, 4, 2, 4, 4, 0, 3, 1, 1, 3, 1, 2, 3, 1, 3, 3, 1,
        4, 3, 1, 3, 4, 1, 4, 3, 4, 3, 4, 4, 0, 0, 2, 1, 0, 2, 2, 0, 2, 3, 0, 2, 4, 0, 2, 0, 4, 2,
        2, 0, 4, 3, 0, 4, 0, 1, 2, 1, 1, 2, 2, 1, 2, 3, 1, 2, 4, 1, 2, 1, 4, 2, 2, 1, 4, 3, 1, 4,
        0, 2, 2, 1, 2, 2, 2, 2, 2, 3, 2, 2, 4, 2, 2, 2, 4, 2, 2, 2, 4, 3, 2, 4, 0, 3, 2, 1, 3, 2,
        2, 3, 2, 3, 3, 2, 4, 3, 2, 3, 4, 2, 2, 3, 4, 3, 3, 4, 0, 0, 3, 1, 0, 3, 2, 0, 3, 3, 0, 3,
        4, 0, 3, 0, 4, 3, 0, 0, 4, 1, 0, 4, 0, 1, 3, 1, 1, 3, 2, 1, 3, 3, 1, 3, 4, 1, 3, 1, 4, 3,
        0, 1, 4, 1, 1, 4, 0, 2, 3, 1, 2, 3, 2, 2, 3, 3, 2, 3, 4, 2, 3, 2, 4, 3, 0, 2, 4, 1, 2, 4,
        0, 3, 3, 1, 3, 3, 2, 3, 3, 3, 3, 3, 4, 3, 3, 3, 4, 3, 0, 3, 4, 1, 3, 4,
    ];
}