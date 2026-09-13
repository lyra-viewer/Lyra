using Lyra.Imaging.Decoding.Decoders;
using Lyra.Imaging.Interop;
using Xunit;

namespace Lyra.Imaging.Tests.Decoding;

/// <summary>
/// When a TIFF is read at its own bit depth rather than through libtiff's RGBA interface.
/// </summary>
public class NativeDepthRoutingTests
{
    private static TiffNative.DirectoryInfo Gray(uint width, uint height, byte capable = 1) =>
        new()
        {
            Width = width, Height = height,
            GrayCapable = capable, RegionCapable = capable, RegionSamples = 1,
            BitsPerSample = 1, SamplesPerPixel = 1
        };

    private static TiffNative.DirectoryInfo Color(uint width, uint height) =>
        new()
        {
            Width = width, Height = height,
            RegionCapable = 1, RegionSamples = 4,
            BitsPerSample = 8, SamplesPerPixel = 3, Photometric = 2
        };

    [Fact]
    public void ALargeFormatScanTakesTheNativePath()
    {
        Assert.True(TiffDecoder.WantsNativeDepth(Gray(23390, 33110))); // A1 at 1000 DPI
        Assert.True(TiffDecoder.WantsNativeDepth(Gray(66220, 93620))); // A0 at 2000 DPI
    }

    [Fact]
    public void ASmallGreyFileKeepsTheExistingPath()
    {
        Assert.False(TiffDecoder.WantsNativeDepth(Gray(2480, 3508)));  // A4 at 300 DPI
    }

    [Fact]
    public void ALargeColourScanTakesItToo()
    {
        Assert.True(TiffDecoder.WantsNativeDepth(Color(23390, 33110)));
    }

    [Fact]
    public void ALayoutWithNoRegionReaderIsLeftAlone()
    {
        Assert.False(TiffDecoder.WantsNativeDepth(Gray(66220, 93620, capable: 0)));
    }

    [Fact]
    public void ADegenerateDirectoryIsRefused()
    {
        Assert.False(TiffDecoder.WantsNativeDepth(Gray(0, 33110)));
        Assert.False(TiffDecoder.WantsNativeDepth(Gray(23390, 0)));
        Assert.False(TiffDecoder.WantsNativeDepth(default));
    }

    [Fact]
    public void TheThresholdSurvivesAnImageThatOverflowsAnInt()
    {
        // 774 Mpx: times four this is 3.1 billion, which is negative as a signed int.
        var info = Gray(23390, 33110);

        Assert.True((long)info.Width * info.Height * 4 > int.MaxValue);
        Assert.True(TiffDecoder.WantsNativeDepth(info));
    }
}