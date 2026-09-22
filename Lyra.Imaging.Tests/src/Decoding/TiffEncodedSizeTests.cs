using System.Runtime.InteropServices;
using Lyra.Imaging.Decoding.Structure;
using Lyra.Imaging.Interop;
using Lyra.Imaging.Tests.Support;
using Xunit;

namespace Lyra.Imaging.Tests.Decoding;

/// <summary>A page's size in the variants list is what it occupies in the file, as for icons.</summary>
public class TiffEncodedSizeTests : IDisposable
{
    private static readonly Lazy<bool> TiffNativeReady = new(() => NativeWrapper.TryLoad(
        "libtiff_native",
        typeof(TiffNative).Assembly,
        () => TiffNative.LoadGrayRegion("lyra-absent.tif", 0, 0, 0, 1, 1, out _, out _))
    );

    private readonly string _path = Path.Combine(Path.GetTempPath(), $"lyra-encoded-{Guid.NewGuid():N}.tif");

    public void Dispose() => File.Delete(_path);

    [Fact]
    public void DescribingByPath_ReportsTheStripBytes()
    {
        Assert.SkipUnless(TiffNativeReady.Value, "libtiff_native not available (native wrappers not built for this platform).");

        File.WriteAllBytes(_path, MinimalImageBuilder.TiffBytes());

        var directories = TiffNative.DescribeDirectories(_path, IntPtr.Zero, 0, out var encodedBytes);

        Assert.Single(directories);
        Assert.NotNull(encodedBytes);
        Assert.Equal([4L], encodedBytes);
    }

    [Fact]
    public void DescribingFromMemory_ReportsTheStripBytes()
    {
        Assert.SkipUnless(TiffNativeReady.Value, "libtiff_native not available (native wrappers not built for this platform).");

        var bytes = MinimalImageBuilder.TiffBytes();
        var handle = GCHandle.Alloc(bytes, GCHandleType.Pinned);

        try
        {
            var directories = TiffNative.DescribeDirectories("<memory>", handle.AddrOfPinnedObject(), (ulong)bytes.Length, out var encodedBytes);

            Assert.Single(directories);
            Assert.NotNull(encodedBytes);
            Assert.Equal([4L], encodedBytes);
        }
        finally
        {
            handle.Free();
        }
    }

    [Fact]
    public void PageVariants_CarryTheEncodedSize()
    {
        var directories = new[] { Page(), Page(), Page() };

        var variants = TiffPageSet.Describe(directories, [0, 1, 2], [1234, 0, 99]);

        Assert.Equal([1234L, null, 99L], variants.Select(v => v.ByteSize));
    }

    [Fact]
    public void PageVariants_HaveNoSize_WhenTheLibraryCannotSay()
    {
        var variants = TiffPageSet.Describe([Page(), Page()], [0, 1], encodedBytes: null);

        Assert.All(variants, v => Assert.Null(v.ByteSize));
    }

    private static TiffNative.DirectoryInfo Page() => new() { Width = 640, Height = 480, BitsPerSample = 8, SamplesPerPixel = 3, Compression = 5 };
}
