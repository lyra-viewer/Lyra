using Lyra.Imaging.Decoding.Decoders.Raw;
using Lyra.Imaging.Tests.Support;
using SkiaSharp;
using Xunit;

namespace Lyra.Imaging.Tests.Decoding;

/// <summary>
/// What an embedded preview without an ICC profile is taken to be. An iPhone ProRAW's preview is
/// Display P3, marked only by EXIF's "uncalibrated" from Apple; read as sRGB it loses saturation.
/// </summary>
public class RawPreviewColorTests
{
    private const ushort Srgb = 1;
    private const ushort Uncalibrated = 0xFFFF;

    private static readonly SKColorSpace DisplayP3 = SKColorSpace.CreateRgb(SKColorSpaceTransferFn.Srgb, SKColorSpaceXyz.DisplayP3);
    private static readonly SKColorSpace AdobeRgb = SKColorSpace.CreateRgb(SKColorSpaceTransferFn.TwoDotTwo, SKColorSpaceXyz.AdobeRgb);

    [Fact]
    public void AnUncalibratedPreviewFromApple_IsDisplayP3()
    {
        Assert.True(SKColorSpace.Equal(DisplayP3, Of(Uncalibrated, "Apple")));
    }

    [Fact]
    public void AnUncalibratedPreviewFromAnyoneElse_IsSrgb()
    {
        Assert.True(Of(Uncalibrated, "Canon").IsSrgb);
    }

    [Fact]
    public void AnSrgbPreview_IsSrgbWhoeverMadeIt()
    {
        Assert.True(Of(Srgb, "Apple").IsSrgb);
    }

    [Fact]
    public void TheDngsOwnStatement_Wins()
    {
        Assert.True(SKColorSpace.Equal(AdobeRgb, Of(Uncalibrated, "Apple", dngPreviewColorSpace: 3)));
    }

    [Fact]
    public void SomethingThatIsNotAJpeg_IsTakenAsSrgb()
    {
        using var stream = new MemoryStream(TempFile.Pattern(256));

        Assert.True(RawPreviewColor.Of(stream, 0).IsSrgb);
    }

    private static SKColorSpace Of(ushort colorSpace, string make, int dngPreviewColorSpace = 0)
    {
        var path = ExifJpegBuilder.Write(colorSpace: colorSpace, make: make);

        try
        {
            using var stream = File.OpenRead(path);
            return RawPreviewColor.Of(stream, dngPreviewColorSpace);
        }
        finally
        {
            File.Delete(path);
        }
    }
}