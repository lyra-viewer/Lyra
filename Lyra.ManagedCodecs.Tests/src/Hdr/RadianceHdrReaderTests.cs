using Lyra.ManagedCodecs.Raster.Hdr;
using Xunit;

namespace Lyra.ManagedCodecs.Tests.Hdr;

public class RadianceHdrReaderTests
{
    // RGBE -> float uses f = 2^(E - 136), so E = 136 gives f = 1 and the stored channel byte maps
    // straight to its float value; E = 137 doubles it. A zero exponent is pure black.
    private const byte E1 = 136; // f = 1.0
    private const byte E2 = 137; // f = 2.0

    [Fact]
    public void DecodesFlatImageTopLeft()
    {
        // Width 2 (< 8) is always stored flat. Stored row-major, top-left origin.
        byte[] hdr = HdrTestImage.Flat(2, 2, '-', '+',
            (10, 20, 30, E1), (40, 0, 0, E1),
            (0, 0, 0, 0), (1, 2, 4, E2));

        var img = RadianceHdrReader.Decode(hdr);

        Assert.Equal(2, img.Width);
        Assert.Equal(2, img.Height);
        Assert.Equal(
            new[]
            {
                10f, 20f, 30f, 1f, 40f, 0f, 0f, 1f,
                0f, 0f, 0f, 1f, 2f, 4f, 8f, 1f,
            },
            img.Pixels);
    }

    [Fact]
    public void AppliesExponentScaling()
    {
        // E2 = 137 -> f = 2: (3, 5, 9) becomes (6, 10, 18).
        byte[] hdr = HdrTestImage.Flat(1, 1, '-', '+', (3, 5, 9, E2));

        var img = RadianceHdrReader.Decode(hdr);

        Assert.Equal(new[] { 6f, 10f, 18f, 1f }, img.Pixels);
    }

    [Fact]
    public void ZeroExponentIsBlack()
    {
        byte[] hdr = HdrTestImage.Flat(1, 1, '-', '+', (200, 200, 200, 0));

        var img = RadianceHdrReader.Decode(hdr);

        Assert.Equal(new[] { 0f, 0f, 0f, 1f }, img.Pixels);
    }

    [Fact]
    public void DecodesRleRunScanline()
    {
        // 8 identical pixels (5, 6, 7), each channel a single run.
        var bytes = HdrTestImage.Header(8, 1);
        HdrTestImage.AppendRleMarker(bytes, 8);
        HdrTestImage.AppendRunPlane(bytes, 5, 8);   // R
        HdrTestImage.AppendRunPlane(bytes, 6, 8);   // G
        HdrTestImage.AppendRunPlane(bytes, 7, 8);   // B
        HdrTestImage.AppendRunPlane(bytes, E1, 8);  // E

        var img = RadianceHdrReader.Decode(bytes.ToArray());

        Assert.Equal(8, img.Width);
        Assert.Equal(1, img.Height);
        for (var x = 0; x < 8; x++)
        {
            Assert.Equal(5f, img.Pixels[(x * 4) + 0]);
            Assert.Equal(6f, img.Pixels[(x * 4) + 1]);
            Assert.Equal(7f, img.Pixels[(x * 4) + 2]);
            Assert.Equal(1f, img.Pixels[(x * 4) + 3]);
        }
    }

    [Fact]
    public void DecodesRleLiteralScanline()
    {
        // R ramps 0..7 (literal packet); G/B/E constant runs.
        byte[] rPlane = [0, 1, 2, 3, 4, 5, 6, 7];

        var bytes = HdrTestImage.Header(8, 1);
        HdrTestImage.AppendRleMarker(bytes, 8);
        HdrTestImage.AppendLiteralPlane(bytes, rPlane);
        HdrTestImage.AppendRunPlane(bytes, 100, 8);  // G
        HdrTestImage.AppendRunPlane(bytes, 50, 8);   // B
        HdrTestImage.AppendRunPlane(bytes, E1, 8);   // E

        var img = RadianceHdrReader.Decode(bytes.ToArray());

        for (var x = 0; x < 8; x++)
        {
            Assert.Equal(x, img.Pixels[(x * 4) + 0]);
            Assert.Equal(100f, img.Pixels[(x * 4) + 1]);
            Assert.Equal(50f, img.Pixels[(x * 4) + 2]);
            Assert.Equal(1f, img.Pixels[(x * 4) + 3]);
        }
    }

    [Fact]
    public void FlipsVerticallyForPositiveY()
    {
        // +Y means the first stored scanline is the visual bottom; output is top-left, so rows swap.
        byte[] hdr = HdrTestImage.Flat(2, 2, '+', '+',
            (1, 1, 1, E1), (2, 2, 2, E1),   // stored row 0 -> visual bottom
            (3, 3, 3, E1), (4, 4, 4, E1));  // stored row 1 -> visual top

        var img = RadianceHdrReader.Decode(hdr);

        Assert.Equal(
            new[]
            {
                3f, 3f, 3f, 1f, 4f, 4f, 4f, 1f,   // top row = stored row 1
                1f, 1f, 1f, 1f, 2f, 2f, 2f, 1f,   // bottom row = stored row 0
            },
            img.Pixels);
    }

    [Fact]
    public void FlipsHorizontallyForNegativeX()
    {
        // -X means pixels run right-to-left; output is left-to-right, so columns reverse.
        byte[] hdr = HdrTestImage.Flat(2, 1, '-', '-', (1, 1, 1, E1), (2, 2, 2, E1));

        var img = RadianceHdrReader.Decode(hdr);

        Assert.Equal(new[] { 2f, 2f, 2f, 1f, 1f, 1f, 1f, 1f }, img.Pixels);
    }

    [Fact]
    public void CanDecodeAcceptsRadianceMagic()
    {
        byte[] hdr = HdrTestImage.Flat(1, 1, '-', '+', (1, 2, 3, E1));
        Assert.True(new RadianceHdrReader().CanDecode(hdr));
    }

    [Fact]
    public void CanDecodeRejectsNonHdr()
    {
        Assert.False(new RadianceHdrReader().CanDecode("not an hdr file"u8));
    }

    [Fact]
    public void ThrowsOnMissingFormatSpecifier()
    {
        byte[] data = "#?RADIANCE\n\n-Y 1 +X 1\n"u8.ToArray();
        Assert.Throws<InvalidDataException>(() => RadianceHdrReader.Decode(data));
    }

    [Fact]
    public void ThrowsOnTruncatedPixelData()
    {
        var bytes = HdrTestImage.Header(2, 2);
        bytes.AddRange([10, 20, 30, E1]); // only one of four pixels present

        Assert.Throws<InvalidDataException>(() => RadianceHdrReader.Decode(bytes.ToArray()));
    }

    [Fact]
    public void DecodesStreamOverload()
    {
        byte[] hdr = HdrTestImage.Flat(1, 1, '-', '+', (12, 34, 56, E1));

        using var stream = new MemoryStream(hdr);
        var img = new RadianceHdrReader().Decode(stream);

        Assert.Equal(new[] { 12f, 34f, 56f, 1f }, img.Pixels);
    }
}
