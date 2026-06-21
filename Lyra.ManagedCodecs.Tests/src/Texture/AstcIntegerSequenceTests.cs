using Lyra.ManagedCodecs.Texture.Blocks.Astc;
using Xunit;

namespace Lyra.ManagedCodecs.Tests.Texture;

/// <summary>
/// Stage-1 unit tests for the ASTC integer-sequence (BISE) foundation: range classification, bit
/// accounting, the plain-bit path, and trit/quint block decoding against known table rows.
/// </summary>
public class AstcIntegerSequenceTests
{
    [Theory]
    [InlineData(1, 0, 0, 1)]   // 2 levels = 2^1
    [InlineData(2, 1, 0, 0)]   // 3 levels = 3·2^0 (one trit)
    [InlineData(4, 0, 1, 0)]   // 5 levels = 5·2^0 (one quint)
    [InlineData(5, 1, 0, 1)]   // 6 levels = 3·2^1
    [InlineData(7, 0, 0, 3)]   // 8 levels = 2^3
    [InlineData(9, 0, 1, 1)]   // 10 levels = 5·2^1
    [InlineData(11, 1, 0, 2)]  // 12 levels = 3·2^2
    [InlineData(255, 0, 0, 8)] // 256 levels = 2^8
    public void CountsForRangeClassifiesQuantLevels(int maxValue, int trits, int quints, int bits)
    {
        AstcIntegerSequence.CountsForRange(maxValue, out var t, out var q, out var b);
        Assert.Equal((trits, quints, bits), (t, q, b));
    }

    [Theory]
    [InlineData(4, 0, 0, 8, 32)] // 4 plain bytes
    [InlineData(5, 1, 0, 0, 8)]  // 5 trits pack into 8 bits
    [InlineData(3, 0, 1, 0, 7)]  // 3 quints pack into 7 bits
    [InlineData(2, 1, 0, 4, 12)] // 2 trit-values with 4 bits each
    public void BitCountMatchesSpec(int count, int trits, int quints, int bits, int expected)
        => Assert.Equal(expected, AstcIntegerSequence.BitCount(count, trits, quints, bits));

    [Fact]
    public void DecodesPlainBitValues()
    {
        // Three 5-bit values packed LSB-first.
        int[] values = [17, 5, 30];
        var block = new byte[16];
        var bit = 0;
        foreach (var v in values)
        {
            for (var i = 0; i < 5; i++, bit++)
                if (((v >> i) & 1) != 0)
                    block[bit >> 3] |= (byte)(1 << (bit & 7));
        }

        Span<int> dst = stackalloc int[3];
        AstcIntegerSequence.Decode(block, 0, 3, trits: 0, quints: 0, bits: 5, dst);
        Assert.Equal(values, dst.ToArray());
    }

    [Theory]
    [InlineData(2, new[] { 2, 0, 0, 0, 0 })]
    [InlineData(4, new[] { 0, 1, 0, 0, 0 })]
    [InlineData(7, new[] { 1, 0, 2, 0, 0 })]
    public void DecodesTritBlockAgainstTable(int packed, int[] expected)
    {
        var block = new byte[16];
        block[0] = (byte)packed; // bits=0 -> the 8-bit packed value indexes the trit table directly

        Span<int> dst = stackalloc int[5];
        AstcIntegerSequence.Decode(block, 0, 5, trits: 1, quints: 0, bits: 0, dst);
        Assert.Equal(expected, dst.ToArray());
    }

    [Theory]
    [InlineData(2, new[] { 2, 0, 0 })]
    [InlineData(5, new[] { 0, 4, 0 })]
    [InlineData(7, new[] { 4, 4, 4 })]
    public void DecodesQuintBlockAgainstTable(int packed, int[] expected)
    {
        var block = new byte[16];
        block[0] = (byte)packed;

        Span<int> dst = stackalloc int[3];
        AstcIntegerSequence.Decode(block, 0, 3, trits: 0, quints: 1, bits: 0, dst);
        Assert.Equal(expected, dst.ToArray());
    }

    [Fact]
    public void DecodesTritValuesWithExtraBits()
    {
        // bits=2: each decoded value = (trit << 2) | low2. Packed trit index 4 -> trits {0,1,0,0,0}.
        // Build a block: for each of 5 values, 2 low bits then the interleaved trit bits.
        // Interleave widths {2,2,1,2,1}; we want packed=4 (binary 00100) -> 3rd interleave field (the
        // single bit at position 4) set. Low bits: pick distinct values 1,2,3,0,1.
        int[] low = [1, 2, 3, 0, 1];
        int[] interleave = [2, 2, 1, 2, 1];
        const int packed = 4;
        var block = new byte[16];
        var bit = 0;

        var packedBitsConsumed = 0;
        for (var i = 0; i < 5; i++)
        {
            Put(low[i], 2);
            Put((packed >> packedBitsConsumed) & ((1 << interleave[i]) - 1), interleave[i]);
            packedBitsConsumed += interleave[i];
        }

        Span<int> dst = stackalloc int[5];
        AstcIntegerSequence.Decode(block, 0, 5, trits: 1, quints: 0, bits: 2, dst);

        int[] trit = [0, 1, 0, 0, 0]; // table row 4
        var expected = new int[5];
        for (var i = 0; i < 5; i++) 
            expected[i] = (trit[i] << 2) | low[i];

        Assert.Equal(expected, dst.ToArray());
        return;

        void Put(int value, int count)
        {
            for (var i = 0; i < count; i++, bit++)
                if (((value >> i) & 1) != 0) 
                    block[bit >> 3] |= (byte)(1 << (bit & 7));
        }
    }
}