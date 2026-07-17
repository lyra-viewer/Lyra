using Lyra.ManagedCodecs.Texture.Blocks.Astc;
using Xunit;

namespace Lyra.ManagedCodecs.Tests.Texture;

/// <summary>
/// Stage-3 unit tests for ASTC block-mode parsing and the weight-path primitives. Full weight
/// reconstruction + infill is validated end-to-end against the astcenc oracle at stage 5.
/// </summary>
public class AstcWeightsTests
{
    [Fact]
    public void ParsesSimpleBlockMode()
    {
        // B4_A2 mode: width=6, height=4, weight range 1, no dual plane. (low bits 0x0141)
        var block = new byte[16];
        block[0] = 0x41;
        block[1] = 0x01;

        var mode = AstcWeights.DecodeMode(block);
        Assert.False(mode.VoidExtent);
        Assert.False(mode.Error);
        Assert.Equal((6, 4), (mode.GridX, mode.GridY));
        Assert.Equal(1, mode.WeightRange);
        Assert.False(mode.DualPlane);
    }

    [Fact]
    public void ParsesDualPlaneBit()
    {
        // Same as above with bit 10 (dual-plane) set.
        var block = new byte[16];
        block[0] = 0x41;
        block[1] = 0x05; // 0x01 | 0x04 (bit 10)

        var mode = AstcWeights.DecodeMode(block);
        Assert.True(mode.DualPlane);
        Assert.Equal((6, 4), (mode.GridX, mode.GridY));
    }

    [Fact]
    public void DetectsVoidExtent()
    {
        var block = new byte[16];
        block[0] = 0xFC;
        block[1] = 0x01; // low 9 bits = 0x1FC

        Assert.True(AstcWeights.DecodeMode(block).VoidExtent);
    }

    [Fact]
    public void FlagsReservedModeAsError()
    {
        // All-zero low bits is a reserved block mode.
        Assert.True(AstcWeights.DecodeMode(new byte[16]).Error);
    }

    [Theory]
    [InlineData(4, 342)]
    [InlineData(5, 256)]
    [InlineData(6, 205)]
    [InlineData(8, 146)]
    [InlineData(10, 114)]
    [InlineData(12, 93)]
    public void ScaleFactorMatchesSpec(int blockDim, int expected) => Assert.Equal(expected, AstcWeights.ScaleFactor(blockDim));

    [Fact]
    public void Reverse128ReversesAllBits()
    {
        var src = new byte[16];
        src[0] = 0x01;  // global bit 0 -> global bit 127 (dst[15] bit 7)
        var dst = new byte[16];
        AstcWeights.Reverse128(src, dst);

        Assert.Equal(0x80, dst[15]);
        for (var i = 0; i < 15; i++)
        {
            Assert.Equal(0, dst[i]);
        }
    }

    [Fact]
    public void Reverse128IsItsOwnInverse()
    {
        var src = new byte[16];
        for (var i = 0; i < 16; i++)
        {
            src[i] = (byte)((i * 37) + 11);
        }

        Span<byte> once = stackalloc byte[16];
        Span<byte> twice = stackalloc byte[16];
        AstcWeights.Reverse128(src, once);
        AstcWeights.Reverse128(once, twice);

        Assert.Equal(src, twice.ToArray());
    }
}
