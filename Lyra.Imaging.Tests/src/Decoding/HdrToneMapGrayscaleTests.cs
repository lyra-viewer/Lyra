using Lyra.Imaging.Content;
using Lyra.Imaging.Decoding.Decoders;
using SkiaSharp;
using Xunit;

namespace Lyra.Imaging.Tests.Decoding;

/// <summary>
/// Regression tests for the HdrToneMap grayscale detection: a pure-red HDR image (G=B=0) must
/// stay red, not be broadcast to gray. The old heuristic treated "G and B all zero" as a
/// single-channel convention; every decoder now replicates single-channel data to R=G=B before
/// tone mapping (OpenEXR RgbaInputFile, SurfaceDecoder.WriteGray, libjxl 4-channel output), so
/// the tone mapper reports true grayscale (R==G==B) and never rewrites channels.
/// Uses hand-built uncompressed Radiance HDR files, so no native wrappers are required.
/// </summary>
public class HdrToneMapGrayscaleTests
{
    // Uncompressed Radiance HDR (flat RGBE scanlines; width < 8 never uses RLE).
    private static string WriteRadianceHdr(byte[] rgbePixel)
    {
        var path = Path.Combine(Path.GetTempPath(), $"lyra-hdr-{Guid.NewGuid():N}.hdr");
        using var fs = File.Create(path);
        fs.Write("#?RADIANCE\nFORMAT=32-bit_rle_rgbe\n\n-Y 2 +X 4\n"u8);
        for (var i = 0; i < 8; i++)
            fs.Write(rgbePixel);
        
        return path;
    }

    private static Composite Decode(string path)
    {
        var composite = new Composite(new FileInfo(path));
        new HdrDecoder().DecodeAsync(composite, CancellationToken.None).GetAwaiter().GetResult();
        return composite;
    }

    private static string GrayScaleFlag(Composite composite)
        => composite.FormatSpecificSnapshot().Single(kv => kv.Key == "GrayScale").Value;

    [Fact]
    public void PureRedHdr_StaysRed_AndIsNotFlaggedGrayscale()
    {
        var path = WriteRadianceHdr([128, 0, 0, 129]); // RGBE for (1.0, 0, 0)

        try
        {
            using var composite = Decode(path);
            var raster = Assert.IsAssignableFrom<RasterContent>(composite.Content);
            using var bitmap = SKBitmap.FromImage(raster.Image);

            var px = bitmap.GetPixel(1, 1);
            Assert.True(px.Red > 200, $"red should survive tone mapping, got {px}");
            Assert.True(px.Green == 0 && px.Blue == 0, $"green/blue must stay 0 (no gray broadcast), got {px}");
            Assert.Equal("False", GrayScaleFlag(composite));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void GrayHdr_IsFlaggedGrayscale()
    {
        var path = WriteRadianceHdr([128, 128, 128, 129]); // RGBE for (1.0, 1.0, 1.0)
        try
        {
            using var composite = Decode(path);
            Assert.IsAssignableFrom<RasterContent>(composite.Content);
            Assert.Equal("True", GrayScaleFlag(composite));
        }
        finally
        {
            File.Delete(path);
        }
    }
}