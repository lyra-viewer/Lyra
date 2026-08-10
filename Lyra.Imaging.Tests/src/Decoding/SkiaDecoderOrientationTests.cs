using Lyra.Imaging.Content;
using Lyra.Imaging.Decoding.Decoders;
using Lyra.Imaging.Tests.Support;
using SkiaSharp;
using Xunit;

namespace Lyra.Imaging.Tests.Decoding;

/// <summary>
/// End-to-end guard for the sideways-photo bug: a JPEG carrying EXIF orientation 6 - what every
/// phone writes when the camera is held on its side - must decode upright, and the orientation
/// must show up in the metadata panel.
/// </summary>
public class SkiaDecoderOrientationTests
{
    [Fact]
    public void Decode_JpegWithOrientation6_RotatesPixelsAndReportsTheTag()
    {
        var path = ExifJpegBuilder.Write(orientation: 6);

        try
        {
            using var composite = Decode(path);
            var content = Assert.IsType<RasterContent>(composite.Content);

            // Rotating 90° CW swaps the axes.
            Assert.Equal(ExifJpegBuilder.Height, content.Image.Width);
            Assert.Equal(ExifJpegBuilder.Width, content.Image.Height);

            // The source is red on the left, blue on the right; after the rotation that has to
            // read as red on top, blue on the bottom.
            using var pixels = content.Image.PeekPixels();
            AssertDominant(pixels.GetPixelColor(4, 2), 'r');
            AssertDominant(pixels.GetPixelColor(4, 13), 'b');

            Assert.Equal(ExifOrientation.Rotate90Cw, composite.ExifInfo?.OrientationValue);

            // What the viewer did, as opposed to what the file asked for - the status line reports
            // this one, and it is only set when the pixels were actually transformed.
            Assert.Equal(ExifOrientation.Rotate90Cw, composite.AppliedOrientation);
            Assert.Contains(
                new ExifEntry("Orientation", "Rotate 90° CW (6)"),
                composite.ExifInfo!.ToKeyValuePairs());
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Decode_JpegWithOrientation1_LeavesPixelsAndAddsNoRow()
    {
        var path = ExifJpegBuilder.Write(orientation: 1);

        try
        {
            using var composite = Decode(path);
            var content = Assert.IsType<RasterContent>(composite.Content);

            Assert.Equal(ExifJpegBuilder.Width, content.Image.Width);
            Assert.Equal(ExifJpegBuilder.Height, content.Image.Height);

            Assert.Equal(ExifOrientation.Normal, composite.ExifInfo?.OrientationValue);
            Assert.DoesNotContain(composite.ExifInfo!.ToKeyValuePairs(), entry => entry.Key == "Orientation");

            // Nothing was transformed, so the status line stays quiet.
            Assert.Equal(ExifOrientation.Normal, composite.AppliedOrientation);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static Composite Decode(string path)
    {
        var composite = new Composite(new FileInfo(path));
        new SkiaDecoder().DecodeAsync(composite, CancellationToken.None).GetAwaiter().GetResult();
        return composite;
    }

    private static void AssertDominant(SKColor color, char channel)
    {
        // JPEG is lossy, so this asserts which channel wins rather than an exact value.
        var (dominant, other1, other2) = channel == 'r'
            ? (color.Red, color.Green, color.Blue)
            : (color.Blue, color.Red, color.Green);

        Assert.True(dominant > other1 + 40 && dominant > other2 + 40,
            $"Expected '{channel}' to dominate, got R={color.Red} G={color.Green} B={color.Blue}.");
    }
}
