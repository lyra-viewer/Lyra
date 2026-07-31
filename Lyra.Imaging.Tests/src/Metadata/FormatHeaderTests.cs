using Lyra.Imaging.Content;
using Lyra.Imaging.Metadata;
using Lyra.Imaging.Tests.Support;
using Xunit;

namespace Lyra.Imaging.Tests.Metadata;

/// <summary>
/// Formats that carry no EXIF still describe themselves in their own headers. Those headers used
/// to be enumerated and ignored - the loops were there, the bodies were TODOs - so a BMP or TGA
/// showed nothing at all in the panel.
/// </summary>
public class FormatHeaderTests
{
    [Fact]
    public void Bmp_ReportsBitDepthAndCompression()
    {
        var path = MinimalImageBuilder.WriteBmp();

        try
        {
            var rows = MetadataProcessor.ParseMetadata(path).ToKeyValuePairs();

            Assert.Contains(new ExifEntry("Bits Per Sample", "24"), rows);
            Assert.Contains(new ExifEntry("Compression", "None"), rows);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Bmp_NamesTheCompressionSchemeWhenThereIsOne()
    {
        var path = MinimalImageBuilder.WriteBmp(bitsPerPixel: 8, compression: 1); // BI_RLE8

        try
        {
            var rows = MetadataProcessor.ParseMetadata(path).ToKeyValuePairs();

            Assert.Contains(new ExifEntry("Bits Per Sample", "8"), rows);
            Assert.Contains(new ExifEntry("Compression", "RLE 8-bit/pixel"), rows);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Tga_ReportsItsPixelDepth()
    {
        var path = MinimalImageBuilder.WriteTga(pixelDepth: 32);

        try
        {
            Assert.Contains(new ExifEntry("Bits Per Sample", "32"),
                MetadataProcessor.ParseMetadata(path).ToKeyValuePairs());
        }
        finally
        {
            File.Delete(path);
        }
    }
}
