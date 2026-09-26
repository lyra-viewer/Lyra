using System.Buffers.Binary;
using System.IO.Compression;
using Lyra.Imaging.Content;
using Lyra.Imaging.Decoding.Decoders;
using Lyra.Imaging.Decoding.Decoders.Tiff;
using Lyra.Imaging.Decoding.Support;
using Lyra.Imaging.Interop;
using Lyra.Imaging.Tests.Support;
using Xunit;

namespace Lyra.Imaging.Tests.Decoding;

/// <summary>
/// Oversized rasters are refused before anything is allocated for them, rather than after.
/// </summary>
public class DecodeSizeGuardTests
{
    private const long Gigabyte = 1024L * 1024 * 1024;

    [Theory]
    [InlineData(1000, 1000, 4, Gigabyte)]
    [InlineData(16384, 16384, 4, Gigabyte)] // exactly 1 GB
    public void RequireAvailableMemory_AllowsWhatFits(long width, long height, int bytesPerPixel, long available) =>
        DecoderValidation.RequireAvailableMemory("Test", width, height, bytesPerPixel, available);

    [Fact]
    public void RequireAvailableMemory_RefusesMoreThanTheMachineHas()
    {
        var thrown = Assert.Throws<InvalidOperationException>(() =>
            DecoderValidation.RequireAvailableMemory("Test", 16385, 16384, 4, Gigabyte));

        Assert.Contains("more than", thrown.Message);
    }

    [Fact]
    public void RequireAvailableMemory_DoesNotOverflowOnHugeDimensions() =>
        Assert.Throws<InvalidOperationException>(() =>
            DecoderValidation.RequireAvailableMemory("Test", uint.MaxValue, uint.MaxValue, 16, Gigabyte));

    [Fact]
    public void RequireAvailableMemory_UnknownMemoryRefusesNothing() =>
        DecoderValidation.RequireAvailableMemory("Test", uint.MaxValue, uint.MaxValue, 16, availableBytes: 0);

    [Theory]
    [InlineData(23170u, 23170u)] // 2,147,395,600 bytes: just under an int
    [InlineData(1u, 1u)]
    public void RequireRgbaWithinOneBitmap_AllowsWhatItHolds(uint width, uint height) =>
        TiffWholeImage.RequireWithinOneBitmap("x.tif", new TiffNative.DirectoryInfo { Width = width, Height = height });

    [Fact]
    public void RequireRgbaWithinOneBitmap_RefusesBeforeLibtiffAllocates()
    {
        Assert.Throws<InvalidOperationException>(() =>
            TiffWholeImage.RequireWithinOneBitmap("x.tif", new TiffNative.DirectoryInfo { Width = 40000, Height = 35900 }));
    }

    [Fact]
    public void PngDeclaringTerabytes_IsRefusedWithoutAllocating()
    {
        var path = Path.Combine(Path.GetTempPath(), $"lyra-bomb-{Guid.NewGuid():N}.png");
        File.WriteAllBytes(path, PngWithHeader(1_000_000, 1_000_000));

        try
        {
            using var composite = new Composite(new FileInfo(path));

            var thrown = Assert.Throws<InvalidOperationException>(() =>
                new SkiaDecoder().DecodeAsync(composite, TestContext.Current.CancellationToken).GetAwaiter().GetResult());

            Assert.Contains("more than", thrown.Message);
            Assert.Null(composite.Content);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void TiffPastOneBitmap_ThumbnailsByStreaming()
    {
        Assert.SkipUnless(
            NativeWrapper.TryLoad("libtiff_native", typeof(TiffNative).Assembly, () => TiffNative.DescribeDirectories("lyra-absent.tif", IntPtr.Zero, 0)),
            "libtiff_native not available (native wrappers not built for this platform)."
        );
        
        using var file = new TempFile(TiffSheetBuilder.Gray(24000, 24000, gray: 200));
        using var thumbnail = new TiffDecoder().DecodeThumbnail(file.Path, 32, TestContext.Current.CancellationToken);

        Assert.NotNull(thumbnail);
        Assert.Equal(32, Math.Max(thumbnail!.Width, thumbnail.Height));
        Assert.Equal(200, thumbnail.GetPixel(thumbnail.Width / 2, thumbnail.Height / 2).Red);
    }

    /// <summary>
    /// A PNG whose header declares <paramref name="width"/> x <paramref name="height"/> and whose
    /// data is one empty row - the shape of a decompression bomb, without the bomb.
    /// </summary>
    private static byte[] PngWithHeader(int width, int height)
    {
        using var png = new MemoryStream();
        png.Write(PngChunks.Signature);

        var header = new byte[13];
        BinaryPrimitives.WriteInt32BigEndian(header, width);
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4), height);
        header[8] = 8; // bit depth
        header[9] = 6; // RGBA

        PngChunks.Write(png, "IHDR", header);

        using (var compressed = new MemoryStream())
        {
            using (var zlib = new ZLibStream(compressed, CompressionLevel.Fastest, leaveOpen: true))
                zlib.Write(new byte[1]); // one filter byte, no pixels

            PngChunks.Write(png, "IDAT", compressed.ToArray());
        }

        PngChunks.Write(png, "IEND", []);
        return png.ToArray();
    }
}
