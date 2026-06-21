using Lyra.ManagedCodecs.Texture.Blocks.Astc;
using Xunit;

namespace Lyra.ManagedCodecs.Tests.Texture;

/// <summary>
/// Stage-2 unit tests for ASTC un-quantization. Expected values are derived directly from the spec
/// procedures (bit replication, and the trit/quint A·B·C reconstruction) rather than the codec under
/// test, so they pin the reconstruction independently.
/// </summary>
public class AstcQuantizationTests
{
    // ---- Weights -> [0,64] (with the C.2.17 upper-half +1 fixup baked in) ----

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 64)] // 1 bit replicated to 6 = 63, then >32 -> +1
    public void WeightBitRange1(int value, int expected)
        => Assert.Equal(expected, AstcQuantization.UnquantizeWeight(value, 1));

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 21)]
    [InlineData(2, 43)] // 42 -> +1
    [InlineData(3, 64)] // 63 -> +1
    public void WeightBitRange3(int value, int expected)
        => Assert.Equal(expected, AstcQuantization.UnquantizeWeight(value, 3));

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 32)]
    [InlineData(2, 64)] // trit table {0,32,63}, 63 -> +1
    public void WeightTritRange2(int value, int expected)
        => Assert.Equal(expected, AstcQuantization.UnquantizeWeight(value, 2));

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 16)]
    [InlineData(2, 32)]
    [InlineData(3, 48)] // 47 -> +1
    [InlineData(4, 64)] // 63 -> +1
    public void WeightQuintRange4(int value, int expected) 
        => Assert.Equal(expected, AstcQuantization.UnquantizeWeight(value, 4));

    // ---- Endpoints -> [0,255] ----

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 36)] // 3 bits replicated to 8
    [InlineData(7, 255)]
    public void EndpointBitRange7(int value, int expected)
        => Assert.Equal(expected, AstcQuantization.UnquantizeEndpoint(value, 7));

    [Fact]
    public void EndpointTritRange5Sequence()
    {
        // The low "m" bit perturbs the high end (spec 'a' term), so the unquantized order is scrambled.
        int[] expected = [0, 255, 51, 204, 102, 153];
        for (var v = 0; v < expected.Length; v++)
        {
            Assert.Equal(expected[v], AstcQuantization.UnquantizeEndpoint(v, 5));
        }
    }

    [Fact]
    public void EndpointQuintRange9Sequence()
    {
        int[] expected = [0, 255, 28, 227, 56, 199, 84, 171, 113, 142];
        for (var v = 0; v < expected.Length; v++)
        {
            Assert.Equal(expected[v], AstcQuantization.UnquantizeEndpoint(v, 9));
        }
    }

    [Fact]
    public void EndpointBitRange255IsIdentity()
    {
        // 8-bit range: the quantized value already is the 8-bit endpoint.
        for (var v = 0; v <= 255; v++)
        {
            Assert.Equal(v, AstcQuantization.UnquantizeEndpoint(v, 255));
        }
    }
}