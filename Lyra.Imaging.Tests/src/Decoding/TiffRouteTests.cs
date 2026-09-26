using Lyra.Imaging.Decoding.Decoders.Tiff;
using Lyra.Imaging.Interop;
using Xunit;

namespace Lyra.Imaging.Tests.Decoding;

/// <summary>
/// The one rule that picks how a TIFF directory is read, for an image, a page and a thumbnail alike.
/// </summary>
public class TiffRouteTests
{
    private static TiffNative.DirectoryInfo Gray(uint width, uint height, bool icc = false, int orientation = 1) =>
        new()
        {
            Width = width, Height = height,
            GrayCapable = 1, RegionCapable = 1, RegionSamples = 1, RgbaCapable = 1, NativeCapable = 1,
            BitsPerSample = 8, SamplesPerPixel = 1, Photometric = 1,
            Traits = TiffNative.DirectoryInfo.PackTraits(icc, orientation)
        };

    [Theory]
    [InlineData(false, 1)]
    [InlineData(true, 1)]
    [InlineData(false, 3)]
    [InlineData(true, 8)]
    public void TraitsReadBackAsPacked(bool icc, int orientation)
    {
        var info = Gray(64, 64, icc, orientation);

        Assert.Equal(icc, info.HasIcc);
        Assert.Equal(orientation, info.Orientation);
    }

    [Fact]
    public void AnOlderBuildsZeroedTraits_ReadAsNoProfile_TopLeft()
    {
        var info = new TiffNative.DirectoryInfo();

        Assert.False(info.HasIcc);
        Assert.Equal(1, info.Orientation);
    }

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(6)]
    public void GrayStoredAnyOtherWayThanTopLeft_IsTurnedUprightThroughRgba(int orientation)
    {
        Assert.Equal(TiffReadPath.WholeImage, TiffRoute.For(Gray(64, 64, orientation: orientation)));
    }

    [Fact]
    public void GrayTooLargeForRgba_ReadsByRegion_HoweverItIsStored()
    {
        Assert.Equal(TiffReadPath.Region, TiffRoute.For(Gray(8200, 8200, orientation: 3)));
    }

    private static TiffNative.DirectoryInfo Rgb(uint width, uint height) => new()
    {
        Width = width, Height = height,
        RegionCapable = 1, RegionSamples = 4, RgbaCapable = 1, NativeCapable = 1,
        BitsPerSample = 8, SamplesPerPixel = 3, Photometric = 2
    };

    private static TiffNative.DirectoryInfo Rgb16(uint width, uint height) => new()
    {
        Width = width, Height = height,
        RgbaCapable = 1, NativeCapable = 1,
        BitsPerSample = 16, SamplesPerPixel = 3, Photometric = 2
    };

    private static TiffNative.DirectoryInfo Float(uint width, uint height) => new()
    {
        Width = width, Height = height,
        NativeCapable = 1, SampleFormat = 3,
        BitsPerSample = 32, SamplesPerPixel = 1, Photometric = 1
    };

    [Fact]
    public void GrayReadsByRegion_AtAnySize()
    {
        Assert.Equal(TiffReadPath.Region, TiffRoute.For(Gray(64, 64)));
        Assert.Equal(TiffReadPath.Region, TiffRoute.For(Gray(8200, 8200)));
    }

    [Fact]
    public void GrayWithAProfile_KeepsTheProfile_ThroughRgba()
    {
        Assert.Equal(TiffReadPath.WholeImage, TiffRoute.For(Gray(64, 64, icc: true)));
    }

    [Fact]
    public void GrayTooLargeForRgba_ReadsByRegion_ProfileOrNot()
    {
        Assert.Equal(TiffReadPath.Region, TiffRoute.For(Gray(8200, 8200, icc: true)));
    }

    [Fact]
    public void GrayTooLargeToHold_Streams()
    {
        Assert.Equal(TiffReadPath.RegionStreamed, TiffRoute.For(Gray(16400, 16400)));
        Assert.Equal(TiffReadPath.RegionStreamed, TiffRoute.For(Gray(16400, 16400, icc: true)));
    }

    [Fact]
    public void SmallColourReadsThroughRgba()
    {
        Assert.Equal(TiffReadPath.WholeImage, TiffRoute.For(Rgb(640, 480)));
    }

    [Fact]
    public void ColourTooLargeForRgba_Streams_ThereIsNoMiddleGround()
    {
        Assert.Equal(TiffReadPath.RegionStreamed, TiffRoute.For(Rgb(8200, 8200)));
    }

    [Fact]
    public void WhatRgbaRefuses_ReadsAtItsOwnLayout()
    {
        Assert.Equal(TiffReadPath.NativeLayout, TiffRoute.For(Float(64, 64)));
    }

    [Fact]
    public void PastOneBitmapWithNoRegionReader_ReadsAtItsOwnLayout()
    {
        Assert.Equal(TiffReadPath.WholeImage, TiffRoute.For(Rgb16(8000, 8000)));
        Assert.Equal(TiffReadPath.NativeLayout, TiffRoute.For(Rgb16(24000, 24000)));
    }

    [Fact]
    public void NothingDescribed_IsTheWholeImageCase()
    {
        Assert.Equal(TiffReadPath.WholeImage, TiffRoute.For(default));
    }

    [Fact]
    public void AFailedRgbaRead_IsRetriedAtItsOwnLayout()
    {
        Assert.True(TiffRoute.RetryAtNativeLayout(Rgb16(64, 64), TiffRead.Failed("refused")));
    }

    [Fact]
    public void ATimedOutRgbaRead_IsNotRetried()
    {
        var timedOut = new TiffRead(false, IntPtr.Zero, TimedOut: true, string.Empty);

        Assert.False(TiffRoute.RetryAtNativeLayout(Rgb16(64, 64), timedOut));
    }

    [Fact]
    public void ALayoutNothingElseReads_IsNotRetried()
    {
        Assert.False(
            TiffRoute.RetryAtNativeLayout(Rgb16(64, 64) with { NativeCapable = 0 }, TiffRead.Failed("refused")));
    }
}