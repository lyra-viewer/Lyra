using Lyra.Imaging.Content;
using Lyra.Imaging.Decoding.Decoders;
using Lyra.Imaging.Interop;
using Lyra.Imaging.Tests.Support;
using Xunit;

namespace Lyra.Imaging.Tests.Interop;

/// <summary>Paths reach the native wrappers as UTF-8 and have to survive the trip.</summary>
public class UnicodePathTests : IDisposable
{
    /// <summary>
    /// Greek, Cyrillic, CJK and an accent at once: no Windows code page holds all four, so the
    /// old ANSI marshaling cannot produce this name whatever the machine is set to.
    /// </summary>
    private const string AwkwardName = "λυρα-тест-画像-café";

    private static readonly Lazy<bool> TiffNativeReady = new(() => NativeWrapper.TryLoad(
        "libtiff_native",
        typeof(TiffNative).Assembly,
        () => TiffNative.LoadGrayRegion(MissingFile, 0, 0, 0, 1, 1, out _, out _))
    );

    private static readonly Lazy<bool> ExrNativeReady = new(() => NativeWrapper.TryLoad(
        "libexr_native",
        typeof(ExrNative).Assembly,
        () => ExrNative.load_exr_rgba(MissingFile, out _, out _, out _, out _))
    );

    /// <summary>
    /// A path the wrapper will refuse, for a probe that only has to reach it. Computed per call
    /// rather than stored: a static field would be read by the initializers above before its own
    /// runs, and nothing here needs the same path twice.
    /// </summary>
    private static string MissingFile => Path.Combine(Path.GetTempPath(), $"lyra-absent-{Guid.NewGuid():N}.bin");

    private readonly List<string> _written = [];

    private string WriteWithAwkwardName(byte[] content, string extension)
    {
        var path = Path.Combine(Path.GetTempPath(), $"{AwkwardName}-{Guid.NewGuid():N}{extension}");
        File.WriteAllBytes(path, content);
        _written.Add(path);

        return path;
    }

    [Fact]
    public void TiffDirectoryListing_ReadsAFileWhoseNameNoCodePageHolds()
    {
        Assert.SkipUnless(TiffNativeReady.Value, "libtiff_native not available (native wrappers not built for this platform).");

        var path = WriteWithAwkwardName(MinimalImageBuilder.TiffBytes(), ".tif");
        
        var directories = TiffNative.DescribeDirectories(path, IntPtr.Zero, 0);

        Assert.Single(directories);
        Assert.Equal(2u, directories[0].Width);
        Assert.Equal(2u, directories[0].Height);
    }

    [Fact]
    public void TiffRegionRead_ReadsAFileWhoseNameNoCodePageHolds()
    {
        Assert.SkipUnless(TiffNativeReady.Value, "libtiff_native not available (native wrappers not built for this platform).");

        var path = WriteWithAwkwardName(MinimalImageBuilder.TiffBytes(), ".tif");

        var read = TiffNative.LoadGrayRegion(path, 0, 0, 0, 2, 2, out var pixels, out var stride);

        try
        {
            Assert.True(read, "the region read refused a file it should have opened");
            Assert.NotEqual(IntPtr.Zero, pixels);
            Assert.True(stride >= 2);
        }
        finally
        {
            if (pixels != IntPtr.Zero)
                TiffNative.free_tiff_pixels(pixels);
        }
    }

    [Fact]
    public void TiffDecode_EndToEnd_ForAFileWhoseNameNoCodePageHolds()
    {
        Assert.SkipUnless(TiffNativeReady.Value, "libtiff_native not available (native wrappers not built for this platform).");

        var path = WriteWithAwkwardName(MinimalImageBuilder.TiffBytes(), ".tif");

        using var composite = new Composite(new FileInfo(path));
        new TiffDecoder().DecodeAsync(composite, TestContext.Current.CancellationToken).GetAwaiter().GetResult();

        Assert.NotNull(composite.Content);
        Assert.Equal(2f, composite.LogicalWidth);
        Assert.Equal(2f, composite.LogicalHeight);
    }

    [Fact]
    public void ExrPathLoad_ReadsAFileWhoseNameNoCodePageHolds()
    {
        Assert.SkipUnless(ExrNativeReady.Value, "libexr_native not available (native wrappers not built for this platform).");
        
        var path = WriteWithAwkwardName(Convert.FromBase64String(SingleChannelExr), ".exr");

        var loaded = ExrNative.load_exr_rgba(path, out var pixels, out var width, out var height, out _);

        try
        {
            Assert.True(loaded, "the EXR load refused a file it should have opened");
            Assert.Equal(4, width);
            Assert.Equal(2, height);
        }
        finally
        {
            if (pixels != IntPtr.Zero)
                ExrNative.free_exr_pixels(pixels);
        }
    }

    /// <summary>
    /// 4x2 uncompressed EXR with one 32-bit float channel. Only its dimensions are asserted on -
    /// what it holds is <see cref="Decoding.ExrGrayscaleTests"/>'s subject, not this one's.
    /// </summary>
    private const string SingleChannelExr =
        "di8xAQIAAABjaGFubmVscwBjaGxpc3QAEwAAAFIAAgAAAAAAAAABAAAAAQAAAABjb21wcmVzc2lvbgBjb21wcmVzc2" +
        "lvbgABAAAAAGRhdGFXaW5kb3cAYm94MmkAEAAAAAAAAAAAAAAAAwAAAAEAAABkaXNwbGF5V2luZG93AGJveDJpABAA" +
        "AAAAAAAAAAAAAAMAAAABAAAAbGluZU9yZGVyAGxpbmVPcmRlcgABAAAAAHBpeGVsQXNwZWN0UmF0aW8AZmxvYXQABA" +
        "AAAAAAgD9zY3JlZW5XaW5kb3dDZW50ZXIAdjJmAAgAAAAAAAAAAAAAAHNjcmVlbldpbmRvd1dpZHRoAGZsb2F0AAQA" +
        "AAAAAIA/ACUBAAAAAAAAPQEAAAAAAAAAAAAAEAAAAAAAAAAAAIA+AAAAPwAAgD8BAAAAEAAAAAAAgD8AAAA/AACAPg" +
        "AAAAA=";

    public void Dispose()
    {
        foreach (var path in _written)
        {
            try
            {
                File.Delete(path);
            }
            catch (IOException)
            {
                // A leftover temp file is not worth failing a test over.
            }
        }
    }
}
