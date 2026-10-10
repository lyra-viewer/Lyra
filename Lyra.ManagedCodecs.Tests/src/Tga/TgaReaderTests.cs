using Lyra.ManagedCodecs.Raster;
using Lyra.ManagedCodecs.Raster.Tga;
using Xunit;

namespace Lyra.ManagedCodecs.Tests.Tga;

public class TgaReaderTests
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

        DecodedImage img = TgaReader.Decode(tga.ToArray());

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

        DecodedImage img = TgaReader.Decode(tga.ToArray());

        Assert.Equal(Expected2X2, img.Pixels);
    }

    [Fact]
    public void DecodesTopRightOriginAsTopLeft()
    {
        // 2x1 visual [red, green], stored right-to-left.
        List<byte> tga = TgaTestImage.Header(0, 2, 0, 0, 2, 1, 24, TgaTestImage.TopRight);
        tga.AddRange([0, 255, 0, /**/ 0, 0, 255]); // green, red (BGR)

        DecodedImage img = TgaReader.Decode(tga.ToArray());

        Assert.Equal(new byte[] { 255, 0, 0, 255, 0, 255, 0, 255 }, img.Pixels);
    }

    [Fact]
    public void Decodes32BitTrueColorWithAlpha()
    {
        List<byte> tga = TgaTestImage.Header(0, 2, 0, 0, 2, 2, 32, TgaTestImage.TopLeft | 8);
        tga.AddRange([0, 0, 255, 255, /**/ 0, 255, 0, 128]); // red a=255, green a=128 (BGRA)
        tga.AddRange([255, 0, 0, 64, /**/ 255, 255, 255, 0]); // blue a=64, white a=0 (BGRA)

        DecodedImage img = TgaReader.Decode(tga.ToArray());

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

        DecodedImage img = TgaReader.Decode(tga.ToArray());

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

        DecodedImage img = TgaReader.Decode(tga.ToArray());

        Assert.Equal(Expected2X2, img.Pixels);
    }

    [Fact]
    public void DecodesRleRunPacket()
    {
        // One run packet: 2 copies of red (high bit set, count-1 = 1).
        List<byte> tga = TgaTestImage.Header(0, 10, 0, 0, 2, 1, 24, TgaTestImage.TopLeft);
        tga.Add(0x81);
        tga.AddRange([0, 0, 255]);

        DecodedImage img = TgaReader.Decode(tga.ToArray());

        Assert.Equal(new byte[] { 255, 0, 0, 255, 255, 0, 0, 255 }, img.Pixels);
    }

    [Fact]
    public void DecodesColorMapped8Bit()
    {
        // 2 color-map entries (BGR): index 0 = red, index 1 = green.
        List<byte> tga = TgaTestImage.Header(1, 1, 2, 24, 2, 1, 8, TgaTestImage.TopLeft);
        tga.AddRange([0, 0, 255, /**/ 0, 255, 0]); // palette
        tga.AddRange([0, 1]); // indices

        DecodedImage img = TgaReader.Decode(tga.ToArray());

        Assert.Equal(new byte[] { 255, 0, 0, 255, 0, 255, 0, 255 }, img.Pixels);
    }

    [Fact]
    public void Decodes16BitTrueColor()
    {
        // Bgra5551 pure red = 0x7C00, little-endian.
        List<byte> tga = TgaTestImage.Header(0, 2, 0, 0, 1, 1, 16, TgaTestImage.TopLeft);
        tga.AddRange([0x00, 0x7C]);

        DecodedImage img = TgaReader.Decode(tga.ToArray());

        Assert.Equal(new byte[] { 255, 0, 0, 255 }, img.Pixels);
    }

    [Fact]
    public void ReadHeaderReportsTheLayout()
    {
        List<byte> tga = TgaTestImage.Header(1, 9, 2, 24, 3, 2, 8, TgaTestImage.BottomLeft);

        TgaHeader header = TgaReader.ReadHeader(tga.ToArray());

        Assert.Equal(TgaImageType.RleColorMapped, header.ImageType);
        Assert.Equal((3, 2), (header.Width, header.Height));
        Assert.Equal((8, 2, 24), (header.PixelDepth, header.CMapLength, header.CMapDepth));
        Assert.Equal(TgaImageOrigin.BottomLeft, header.Origin);
    }

    [Fact]
    public void ReadHeaderRejectsAFileShorterThanAHeader() =>
        Assert.Throws<InvalidDataException>(() => TgaReader.ReadHeader(new byte[17]));

    // One all-zero pixel: alpha decodes to 0 where the format carries it, and 255 where it does not.
    [Theory]
    [InlineData(2, 24, 0, 0)]
    [InlineData(2, 24, 0, 8)] // the descriptor claims alpha a 24-bit pixel has no room for
    [InlineData(2, 32, 0, 8)]
    [InlineData(2, 32, 0, 0)] // 32-bit true color carries alpha even when the descriptor says none
    [InlineData(2, 16, 0, 1)]
    [InlineData(2, 16, 0, 0)]
    [InlineData(3, 8,  0, 0)]
    [InlineData(3, 16, 0, 8)]
    [InlineData(1, 8, 24, 8)] // a 24-bit palette is opaque whatever the descriptor says
    [InlineData(1, 8, 32, 0)]
    [InlineData(1, 8, 16, 1)]
    [InlineData(1, 8, 16, 0)]
    public void HasAlphaSaysWhatTheDecodeHolds(byte imageType, byte pixelDepth, byte cMapDepth, byte alphaBits)
    {
        bool mapped = imageType == 1;
        List<byte> tga = TgaTestImage.Header(mapped ? (byte)1 : (byte)0, imageType, mapped ? (ushort)1 : (ushort)0, cMapDepth, 1, 1, pixelDepth, (byte)(TgaTestImage.TopLeft | alphaBits));
        tga.AddRange(new byte[cMapDepth / 8]);
        tga.AddRange(new byte[(pixelDepth + 7) / 8]);

        byte[] file = [.. tga];
        bool transparent = TgaReader.Decode(file).Pixels[3] < 255;

        Assert.Equal(transparent, TgaReader.ReadHeader(file).HasAlpha);
    }

    [Fact]
    public void IsVersion2DetectsTheFooter()
    {
        List<byte> tga = TgaTestImage.Header(0, 2, 0, 0, 1, 1, 24, TgaTestImage.TopLeft);
        tga.AddRange([0, 0, 255]);

        Assert.False(TgaReader.IsVersion2(tga.ToArray()));
        Assert.True(TgaReader.IsVersion2([.. tga, .. TgaTestImage.Footer]));
    }

    [Fact]
    public void AVersion2FileDecodesAsBefore()
    {
        List<byte> tga = TgaTestImage.Header(0, 2, 0, 0, 1, 1, 24, TgaTestImage.TopLeft);
        tga.AddRange([0, 0, 255]);

        DecodedImage img = TgaReader.Decode([.. tga, .. TgaTestImage.Footer]);

        Assert.Equal(new byte[] { 255, 0, 0, 255 }, img.Pixels);
    }

    [Fact]
    public void ATruncatedFileDecodesAsFarAsItGoes()
    {
        // 2x2 top-left, three of the four pixels present: red, green, blue.
        List<byte> tga = TgaTestImage.Header(0, 2, 0, 0, 2, 2, 24, TgaTestImage.TopLeft);
        tga.AddRange([0, 0, 255, /**/ 0, 255, 0, /**/ 255, 0, 0]);

        DecodedImage img = TgaReader.Decode(tga.ToArray(), out bool truncated);

        Assert.True(truncated);
        Assert.Equal(new byte[] { 255, 0, 0, 255, 0, 255, 0, 255, 0, 0, 255, 255, 0, 0, 0, 0 }, img.Pixels);
        Assert.Throws<InvalidDataException>(() => TgaReader.Decode(tga.ToArray()));
    }

    [Fact]
    public void ATruncatedBottomUpFileMissesItsTopRows()
    {
        // Stored bottom row first; only that row arrives, so the visual top row is what is missing.
        List<byte> tga = TgaTestImage.Header(0, 2, 0, 0, 2, 2, 24, TgaTestImage.BottomLeft);
        tga.AddRange([255, 0, 0, /**/ 255, 255, 255]);

        DecodedImage img = TgaReader.Decode(tga.ToArray(), out bool truncated);

        Assert.True(truncated);
        Assert.Equal(new byte[] { 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 255, 255, 255, 255, 255, 255 }, img.Pixels);
    }

    [Fact]
    public void ATruncatedRleFileDecodesAsFarAsItGoes()
    {
        // A literal packet promising four pixels, of which the data holds two.
        List<byte> tga = TgaTestImage.Header(0, 10, 0, 0, 2, 2, 24, TgaTestImage.TopLeft);
        tga.AddRange([0x03, 0, 0, 255, /**/ 0, 255, 0]);

        DecodedImage img = TgaReader.Decode(tga.ToArray(), out bool truncated);

        Assert.True(truncated);
        Assert.Equal(new byte[] { 255, 0, 0, 255, 0, 255, 0, 255 }, img.Pixels[..8]);
        Assert.All(img.Pixels[8..], b => Assert.Equal(0, b));
    }

    [Fact]
    public void AFileEndingBeforeItsPixelsIsRejected()
    {
        List<byte> tga = TgaTestImage.Header(0, 2, 0, 0, 2, 2, 24, TgaTestImage.TopLeft);

        Assert.Throws<InvalidDataException>(() => TgaReader.Decode(tga.ToArray(), out _));
    }

    [Fact]
    public void CanDecodeAcceptsValidHeader()
    {
        List<byte> tga = TgaTestImage.Header(0, 2, 0, 0, 2, 2, 24, TgaTestImage.TopLeft);
        Assert.True(TgaReader.CanDecode(tga.ToArray()));
    }

    [Fact]
    public void CanDecodeRejectsNonTga()
    {
        // Invalid color map type (5) — not a plausible TGA header.
        byte[] notTga = [0, 5, 99, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1, 0, 1, 0, 24, 0x20];
        Assert.False(TgaReader.CanDecode(notTga));
    }
}