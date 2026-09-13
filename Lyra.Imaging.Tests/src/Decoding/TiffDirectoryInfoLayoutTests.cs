using System.Runtime.InteropServices;
using Lyra.Imaging.Interop;
using Xunit;

namespace Lyra.Imaging.Tests.Decoding;

public class TiffDirectoryInfoLayoutTests
{
    [Fact]
    public void TheStructIsTheSizeTheNativeSideDeclares()
    {
        Assert.Equal(48, Marshal.SizeOf<TiffNative.DirectoryInfo>());
    }
    
    [Theory]
    [InlineData(nameof(TiffNative.DirectoryInfo.Width), 0)]
    [InlineData(nameof(TiffNative.DirectoryInfo.SubfileType), 8)]
    [InlineData(nameof(TiffNative.DirectoryInfo.PageNumber), 24)]
    [InlineData(nameof(TiffNative.DirectoryInfo.Compression), 38)]
    [InlineData(nameof(TiffNative.DirectoryInfo.IsTiled), 40)]
    [InlineData(nameof(TiffNative.DirectoryInfo.GrayCapable), 41)]
    [InlineData(nameof(TiffNative.DirectoryInfo.RgbaCapable), 42)]
    [InlineData(nameof(TiffNative.DirectoryInfo.NativeCapable), 43)]
    [InlineData(nameof(TiffNative.DirectoryInfo.RegionCapable), 44)]
    [InlineData(nameof(TiffNative.DirectoryInfo.RegionSamples), 45)]
    [InlineData(nameof(TiffNative.DirectoryInfo.RegionPremultiplied), 46)]
    public void EachFieldSitsWhereTheHeaderPutsIt(string field, int offset)
    {
        Assert.Equal(offset, Marshal.OffsetOf<TiffNative.DirectoryInfo>(field).ToInt32());
    }
}
