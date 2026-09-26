using Lyra.Imaging.Content;
using Lyra.Imaging.Decoding.Decoders.Tiff;
using Lyra.Imaging.Interop;
using Xunit;

namespace Lyra.Imaging.Tests.Decoding;

/// <summary>
/// What a directory read at its own sample layout is allowed to cost.
/// </summary>
public class NativeLayoutBudgetTests
{
    private static TiffNative.DirectoryInfo Odd(uint width, uint height, ushort bits, ushort samples, ushort format = 1) =>
        new()
        {
            Width = width, Height = height,
            BitsPerSample = bits, SamplesPerPixel = samples, SampleFormat = format,
            NativeCapable = 1, Photometric = (ushort)(samples >= 3 ? 2 : 1)
        };

    [Fact]
    public void ThePeakCountsThePackedFormAndTheConvertedOne()
    {
        // 64 Mpx at 12-bit x3 packs to 288 MB and converts to 256 MB of RGBA8.
        var peak = TiffNativeLayout.PeakBytes(Odd(8192, 8192, 12, 3), isFloat: false);

        Assert.Equal(288L * 1024 * 1024 + 256L * 1024 * 1024, peak);
    }

    [Fact]
    public void FloatIsCountedAsSixteenBytesAPixel()
    {
        var info = Odd(1024, 1024, 32, 3, format: 3);

        Assert.Equal(12L * 1024 * 1024 + 16L * 1024 * 1024, TiffNativeLayout.PeakBytes(info, isFloat: true));
    }

    [Fact]
    public void OneSampleIsCountedAsGrey()
    {
        // 1 Mpx at 14-bit: 1.75 MB packed, 1 MB of Gray8.
        var info = Odd(1024, 1024, 14, 1);

        Assert.Equal(1835008L + 1048576L, TiffNativeLayout.PeakBytes(info, isFloat: false));
    }
    
    [Fact]
    public void ThePeakSurvivesAnImageThatOverflowsAnInt()
    {
        var peak = TiffNativeLayout.PeakBytes(Odd(40000, 40000, 12, 3), isFloat: false);

        Assert.True(peak > int.MaxValue);
        Assert.Equal(1_600_000_000L * 9 / 2 + 1_600_000_000L * 4, peak);
    }
    
    [Fact]
    public void ASheetTooLargeToHoldIsRefusedBeforeAnythingIsAllocated()
    {
        var info = Odd(40000, 40000, 12, 3);

        Assert.True(TiffNativeLayout.PeakBytes(info, isFloat: false) > TiffNativeLayout.BudgetBytes);

        var ex = Assert.Throws<LoadFailureException>(() => TiffNativeLayout.RequireWithinBudget("sheet.tif", info, isFloat: false));

        Assert.Contains("at peak", ex.Message);
        Assert.Equal(LoadFailureKind.TooLarge, ex.Kind);
    }
    
    [Fact]
    public void AnOrdinaryOddLayoutImageIsAllowed()
    {
        var info = Odd(4096, 4096, 12, 3);

        Assert.True(TiffNativeLayout.PeakBytes(info, isFloat: false) <= TiffNativeLayout.BudgetBytes);

        TiffNativeLayout.RequireWithinBudget("scan.tif", info, isFloat: false);
    }
    
    [Fact]
    public void ADegenerateDirectoryIsRefused()
    {
        Assert.Throws<InvalidOperationException>(() => TiffNativeLayout.RequireWithinBudget("empty.tif", Odd(0, 4096, 12, 3), isFloat: false));
        Assert.Throws<InvalidOperationException>(() => TiffNativeLayout.RequireWithinBudget("empty.tif", Odd(4096, 0, 12, 3), isFloat: false));
        Assert.Throws<InvalidOperationException>(() => TiffNativeLayout.RequireWithinBudget("huge.tif", Odd(uint.MaxValue, 4096, 12, 3), isFloat: false));
    }
}
