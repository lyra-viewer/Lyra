using Lyra.ManagedCodecs.Tga;
using Xunit;

namespace Lyra.ManagedCodecs.Tests.Tga;

public class TgaDecoderTests
{
    // The canonical 2x2 image used across tests, in RGBA top-left order:
    //   (0,0) red   (1,0) green
    //   (0,1) blue  (1,1) white
    private static readonly byte[] Expected2X2 =
    [
        255, 0, 0, 255, 0, 255, 0, 255,
        0, 0, 255, 255, 255, 255, 255, 255,
    ];

    [Fact]
    public void Decodes24BitTrueColorTopLeft()
    {
        List<byte> tga = TgaTestImage.Header(0, 2, 0, 0, 2, 2, 24, TgaTestImage.TopLeft);
        tga.AddRange([0, 0, 255, /**/ 0, 255, 0]); // row 0: red, green (BGR)
        tga.AddRange([255, 0, 0, /**/ 255, 255, 255]); // row 1: blue, white (BGR)

        DecodedImage img = TgaDecoder.Decode(tga.ToArray());

        Assert.Equal(2, img.Width);
        Assert.Equal(2, img.Height);
        Assert.Equal(Expected2X2, img.Pixels);
    }

    [Fact]
    public void DecodesBottomLeftOriginAsTopLeft()
    {
        // Same visual image, stored bottom-up: first stored row is the visual bottom row.
        List<byte> tga = TgaTestImage.Header(0, 2, 0, 0, 2, 2, 24, TgaTestImage.BottomLeft);
        tga.AddRange([255, 0, 0, /**/ 255, 255, 255]); // stored row 0 = visual bottom: blue, white
        tga.AddRange([0, 0, 255, /**/ 0, 255, 0]); // stored row 1 = visual top: red, green

        DecodedImage img = TgaDecoder.Decode(tga.ToArray());

        Assert.Equal(Expected2X2, img.Pixels);
    }

    [Fact]
    public void DecodesTopRightOriginAsTopLeft()
    {
        // 2x1 visual [red, green], stored right-to-left.
        List<byte> tga = TgaTestImage.Header(0, 2, 0, 0, 2, 1, 24, TgaTestImage.TopRight);
        tga.AddRange([0, 255, 0, /**/ 0, 0, 255]); // green, red (BGR)

        DecodedImage img = TgaDecoder.Decode(tga.ToArray());

        Assert.Equal(new byte[] { 255, 0, 0, 255, 0, 255, 0, 255 }, img.Pixels);
    }

    [Fact]
    public void Decodes32BitTrueColorWithAlpha()
    {
        List<byte> tga = TgaTestImage.Header(0, 2, 0, 0, 2, 2, 32, (byte)(TgaTestImage.TopLeft | 8));
        tga.AddRange([0, 0, 255, 255, /**/ 0, 255, 0, 128]); // red a=255, green a=128 (BGRA)
        tga.AddRange([255, 0, 0, 64, /**/ 255, 255, 255, 0]); // blue a=64, white a=0 (BGRA)

        DecodedImage img = TgaDecoder.Decode(tga.ToArray());

        Assert.Equal(
            new byte[]
            {
                255, 0, 0, 255, 0, 255, 0, 128,
                0, 0, 255, 64, 255, 255, 255, 0,
            },
            img.Pixels);
    }

    [Fact]
    public void Decodes8BitGrayscale()
    {
        List<byte> tga = TgaTestImage.Header(0, 3, 0, 0, 2, 2, 8, TgaTestImage.TopLeft);
        tga.AddRange([0, 85, 170, 255]);

        DecodedImage img = TgaDecoder.Decode(tga.ToArray());

        Assert.Equal(
            new byte[]
            {
                0, 0, 0, 255, 85, 85, 85, 255,
                170, 170, 170, 255, 255, 255, 255, 255,
            },
            img.Pixels);
    }

    [Fact]
    public void DecodesRleLiteralPacket()
    {
        // One literal packet of 4 distinct pixels (count-1 = 3, high bit clear).
        List<byte> tga = TgaTestImage.Header(0, 10, 0, 0, 2, 2, 24, TgaTestImage.TopLeft);
        tga.Add(0x03);
        tga.AddRange([0, 0, 255, /**/ 0, 255, 0, /**/ 255, 0, 0, /**/ 255, 255, 255]);

        DecodedImage img = TgaDecoder.Decode(tga.ToArray());

        Assert.Equal(Expected2X2, img.Pixels);
    }

    [Fact]
    public void DecodesRleRunPacket()
    {
        // One run packet: 2 copies of red (high bit set, count-1 = 1).
        List<byte> tga = TgaTestImage.Header(0, 10, 0, 0, 2, 1, 24, TgaTestImage.TopLeft);
        tga.Add(0x81);
        tga.AddRange([0, 0, 255]);

        DecodedImage img = TgaDecoder.Decode(tga.ToArray());

        Assert.Equal(new byte[] { 255, 0, 0, 255, 255, 0, 0, 255 }, img.Pixels);
    }

    [Fact]
    public void DecodesColorMapped8Bit()
    {
        // 2 color-map entries (BGR): index 0 = red, index 1 = green.
        List<byte> tga = TgaTestImage.Header(1, 1, 2, 24, 2, 1, 8, TgaTestImage.TopLeft);
        tga.AddRange([0, 0, 255, /**/ 0, 255, 0]); // palette
        tga.AddRange([0, 1]); // indices

        DecodedImage img = TgaDecoder.Decode(tga.ToArray());

        Assert.Equal(new byte[] { 255, 0, 0, 255, 0, 255, 0, 255 }, img.Pixels);
    }

    [Fact]
    public void Decodes16BitTrueColor()
    {
        // Bgra5551 pure red = 0x7C00, little-endian.
        List<byte> tga = TgaTestImage.Header(0, 2, 0, 0, 1, 1, 16, TgaTestImage.TopLeft);
        tga.AddRange([0x00, 0x7C]);

        DecodedImage img = TgaDecoder.Decode(tga.ToArray());

        Assert.Equal(new byte[] { 255, 0, 0, 255 }, img.Pixels);
    }

    [Fact]
    public void CanDecodeAcceptsValidHeader()
    {
        List<byte> tga = TgaTestImage.Header(0, 2, 0, 0, 2, 2, 24, TgaTestImage.TopLeft);
        Assert.True(new TgaDecoder().CanDecode(tga.ToArray()));
    }

    [Fact]
    public void CanDecodeRejectsNonTga()
    {
        // Invalid color map type (5) — not a plausible TGA header.
        byte[] notTga = [0, 5, 99, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1, 0, 1, 0, 24, 0x20];
        Assert.False(new TgaDecoder().CanDecode(notTga));
    }
}