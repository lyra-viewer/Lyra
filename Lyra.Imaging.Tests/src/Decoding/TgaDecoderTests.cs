using Lyra.Imaging.Content;
using Lyra.Imaging.Decoding.Decoders;
using Lyra.Imaging.Tests.Support;
using Lyra.ManagedCodecs.Tests.Tga;
using SkiaSharp;
using Xunit;

namespace Lyra.Imaging.Tests.Decoding;

public class TgaDecoderTests
{
    [Fact]
    public void ATrueColorTga_IsDescribed()
    {
        // 2x1 RLE 32-bit, one literal packet of two BGRA pixels, an image ID, and the TGA 2.0 footer.
        var tga = TgaTestImage.Header(0, 10, 0, 0, 2, 1, 32, TgaTestImage.TopLeft | 8);
        tga[0] = 4;
        tga.AddRange("Lyra"u8.ToArray());
        tga.AddRange([0x01, 0, 0, 255, 255, /**/ 255, 0, 0, 128]);
        tga.AddRange(TgaTestImage.Footer);

        var facts = FactsOf([.. tga]);

        Assert.Equal("TGA 2.0", facts["Format"]);
        Assert.Equal("True color", facts["Image Type"]);
        Assert.Equal("RLE", facts["Compression"]);
        Assert.Equal("32-bit", facts["Bit Depth"]);
        Assert.Equal("Yes", facts["Has Alpha"]);
        Assert.Equal("Top left", facts["Origin"]);
        Assert.Equal("Lyra", facts["Image ID"]);
        Assert.False(facts.ContainsKey("Palette"));
    }

    [Fact]
    public void AColorMappedTga_IsDescribedWithItsPalette()
    {
        var tga = TgaTestImage.Header(1, 1, 2, 24, 2, 1, 8, TgaTestImage.BottomLeft);
        tga.AddRange([0, 0, 255, /**/ 255, 0, 0]);
        tga.AddRange([0, 1]);

        var facts = FactsOf([.. tga]);

        Assert.Equal("TGA 1.0", facts["Format"]);
        Assert.Equal("Color-mapped", facts["Image Type"]);
        Assert.Equal("None", facts["Compression"]);
        Assert.Equal("8-bit indices", facts["Bit Depth"]);
        Assert.Equal("2 colors, 24-bit", facts["Palette"]);
        Assert.Equal("No", facts["Has Alpha"]);
        Assert.Equal("Bottom left", facts["Origin"]);
        Assert.False(facts.ContainsKey("Image ID"));
    }

    [Fact]
    public void AnImageIdThatIsNotText_IsNotShown()
    {
        var tga = TgaTestImage.Header(0, 3, 0, 0, 1, 1, 8, TgaTestImage.TopLeft);
        tga[0] = 3;
        tga.AddRange([0x01, 0xFE, 0x7F]);
        tga.Add(200);

        var facts = FactsOf([.. tga]);

        Assert.Equal("Grayscale", facts["Image Type"]);
        Assert.False(facts.ContainsKey("Image ID"));
    }

    [Fact]
    public void ATruncatedTga_IsShownAndFlagged()
    {
        var tga = TgaTestImage.Header(0, 2, 0, 0, 2, 2, 24, TgaTestImage.TopLeft);
        tga.AddRange([0, 0, 255, /**/ 0, 255, 0, /**/ 255, 0, 0]);

        using var temp = new TempFile([.. tga]);
        using var composite = new Composite(new FileInfo(temp.Path));
        new TgaDecoder().DecodeAsync(composite, CancellationToken.None).GetAwaiter().GetResult();

        Assert.IsType<RasterContent>(composite.Content);
        Assert.Equal(LoadWarning.PartiallyDecoded("The file ends before the image does"), composite.Warning);
    }

    [Fact]
    public void ATgaThatCannotBeDecoded_FailsWithItsOwnReason()
    {
        var tga = TgaTestImage.Header(0, 2, 0, 0, 2, 2, 7, TgaTestImage.TopLeft);
        tga.AddRange(new byte[16]);

        using var temp = new TempFile([.. tga]);
        using var composite = new Composite(new FileInfo(temp.Path));

        var thrown = Assert.Throws<LoadFailureException>(() => new TgaDecoder().DecodeAsync(composite, CancellationToken.None).GetAwaiter().GetResult());
        Assert.Equal("Unsupported pixel depth 7.", thrown.Message);
    }

    [Fact]
    public void AnotherFormatNamedTga_HasNoTgaFacts()
    {
        using var bitmap = new SKBitmap(2, 2);
        using var image = SKImage.FromBitmap(bitmap);
        using var png = image.Encode(SKEncodedImageFormat.Png, 100);

        var facts = FactsOf(png.ToArray());

        Assert.False(facts.ContainsKey("Image Type"));
    }

    private static Dictionary<string, string> FactsOf(byte[] file)
    {
        using var temp = new TempFile(file);
        using var composite = new Composite(new FileInfo(temp.Path));

        new TgaDecoder().DecodeAsync(composite, CancellationToken.None).GetAwaiter().GetResult();

        Assert.NotNull(composite.Content);
        return composite.FormatSpecificSnapshot().ToDictionary(p => p.Key, p => p.Value);
    }
}
