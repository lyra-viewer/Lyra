using Lyra.Imaging.Content;
using Lyra.Imaging.Decoding.Decoders;
using Lyra.Imaging.Decoding.Decoders.Tiff;
using Lyra.Imaging.Interop;
using Lyra.Imaging.Tests.Support;
using SkiaSharp;
using Xunit;

namespace Lyra.Imaging.Tests.Decoding;

/// <summary>
/// Which read path each kind of TIFF takes, end to end through the real library - as a single
/// image, as a page of a document, and as a thumbnail. The paths leave different fingerprints:
/// libtiff's RGBA interface hands back premultiplied RGBA, a gray region read hands back one byte
/// a pixel, a sheet too large to hold comes back as a preview plus tiles, and a float layout comes
/// back scene-referred. One rule decides for all three (<see cref="TiffRoute"/>), so an image and a
/// page of the same shape now come out the same way.
/// </summary>
public class TiffReadPathTests
{
    private static readonly Lazy<bool> TiffNativeReady = new(() => NativeWrapper.TryLoad("libtiff_native", typeof(TiffNative).Assembly,
        () => TiffNative.DescribeDirectories("lyra-absent.tif", IntPtr.Zero, 0))
    );

    private const string NotBuilt = "libtiff_native not available (native wrappers not built for this platform).";

    /// <summary>Just over the size at which a gray sheet is read at its own depth rather than as RGBA.</summary>
    private const int OverRgbaCeiling = 8200;

    /// <summary>Just over the size at which a gray sheet is streamed rather than held.</summary>
    private const int OverStreamingCeiling = 16400;

    #region Single image

    [Fact]
    public void SmallGray_AsImage_ReadsGrayRegion()
    {
        AssertGrayRegion(DecodeImage(TiffBuilder.Gray(64, 64)));
    }

    [Fact]
    public void Small16BitGray_AsImage_ReadsGrayRegion()
    {
        AssertGrayRegion(DecodeImage(TiffBuilder.Gray(64, 64, bits: 16)));
    }

    [Fact]
    public void GrayWithAProfile_AsImage_ReadsThroughRgba()
    {
        AssertRgba(DecodeImage(TiffBuilder.GrayWithIcc(64, 64)));
    }

    [Theory]
    [InlineData(3)]
    [InlineData(4)]
    public void GrayStoredUpsideDown_AsImage_ReadsThroughRgba_WhichTurnsItUpright(ushort orientation)
    {
        AssertRgba(DecodeImage(TiffBuilder.GrayOriented(64, 64, orientation)));
    }

    [Fact]
    public void SmallGray_AsImage_IsFetchedWholeFirst()
    {
        Assert.SkipUnless(TiffNativeReady.Value, NotBuilt);

        var tiff = TiffBuilder.Build(TiffBuilder.Gray(512, 4096));

        using var file = new TempFile(tiff);
        using var composite = new Composite(new FileInfo(file.Path));

        composite.BeginLoadTiming();
        new TiffDecoder().DecodeAsync(composite, TestContext.Current.CancellationToken).GetAwaiter().GetResult();

        {
            Assert.Equal(SKColorType.Gray8, Assert.IsType<RasterContent>(composite.Content).Image.ColorType);
            Assert.Equal(tiff.Length, composite.Timing.TransferBytesRead);
            Assert.NotNull(composite.ExifInfo);
        }
    }

    [Fact]
    public void ARegionReadFromMemory_MatchesTheSameReadFromThePath()
    {
        Assert.SkipUnless(TiffNativeReady.Value, NotBuilt);

        var tiff = TiffBuilder.Build(TiffBuilder.Gray(300, 200));
        using var file = new TempFile(tiff);

        Assert.True(TiffNative.LoadRegion(file.Path, 0, false, 10, 20, 100, 50, out var fromPath, out var pathStride));

        var pinned = System.Runtime.InteropServices.GCHandle.Alloc(tiff, System.Runtime.InteropServices.GCHandleType.Pinned);

        try
        {
            Assert.True(TiffNative.LoadRegionFromMemory(pinned.AddrOfPinnedObject(), (ulong)tiff.Length, 0, false, 10, 20, 100, 50, out var fromMemory, out var memoryStride));

            try
            {
                Assert.Equal(pathStride, memoryStride);

                unsafe
                {
                    var expected = new ReadOnlySpan<byte>((void*)fromPath, (int)(pathStride * 50));
                    var actual = new ReadOnlySpan<byte>((void*)fromMemory, (int)(memoryStride * 50));

                    Assert.True(expected.SequenceEqual(actual));
                }
            }
            finally
            {
                TiffNative.free_tiff_pixels(fromMemory);
            }
        }
        finally
        {
            pinned.Free();
            TiffNative.free_tiff_pixels(fromPath);
        }
    }

    [Fact]
    public void SmallRgb_AsImage_ReadsThroughRgba()
    {
        AssertRgba(DecodeImage(TiffBuilder.Rgb(64, 64)));
    }

    [Fact]
    public void LargeGray_AsImage_ReadsGrayRegion()
    {
        AssertGrayRegion(DecodeImage(TiffBuilder.Gray(OverRgbaCeiling, OverRgbaCeiling)));
    }

    [Fact]
    public void HugeGray_AsImage_Streams()
    {
        AssertStreamed(DecodeImage(TiffBuilder.Gray(OverStreamingCeiling, OverStreamingCeiling)));
    }

    [Fact]
    public void FloatGray_AsImage_ReadsItsOwnLayout()
    {
        AssertSceneReferred(DecodeImage(TiffBuilder.FloatGray(64, 64)));
    }

    #endregion

    #region Page of a document

    [Fact]
    public void SmallGray_AsPage_ReadsGrayRegion()
    {
        AssertGrayRegion(DecodeFirstPage(TiffBuilder.Gray(64, 64)));
    }

    [Fact]
    public void Small16BitGray_AsPage_ReadsGrayRegion()
    {
        AssertGrayRegion(DecodeFirstPage(TiffBuilder.Gray(64, 64, bits: 16)));
    }

    [Fact]
    public void GrayWithAProfile_AsPage_ReadsThroughRgba()
    {
        AssertRgba(DecodeFirstPage(TiffBuilder.GrayWithIcc(64, 64)));
    }

    [Fact]
    public void GrayStoredUpsideDown_AsPage_ReadsThroughRgba()
    {
        AssertRgba(DecodeFirstPage(TiffBuilder.GrayOriented(64, 64, 3)));
    }

    [Fact]
    public void SmallRgb_AsPage_ReadsThroughRgba()
    {
        AssertRgba(DecodeFirstPage(TiffBuilder.Rgb(64, 64)));
    }

    [Fact]
    public void LargeGray_AsPage_ReadsGrayRegion()
    {
        AssertGrayRegion(DecodeFirstPage(TiffBuilder.Gray(OverRgbaCeiling, OverRgbaCeiling)));
    }

    [Fact]
    public void HugeGray_AsPage_Streams()
    {
        AssertStreamed(DecodeFirstPage(TiffBuilder.Gray(OverStreamingCeiling, OverStreamingCeiling)));
    }

    [Fact]
    public void FloatGray_AsPage_ReadsItsOwnLayout()
    {
        AssertSceneReferred(DecodeFirstPage(TiffBuilder.FloatGray(64, 64)));
    }

    #endregion

    #region Thumbnail

    [Fact]
    public void SmallGray_ThumbnailsByRegion()
    {
        using var thumbnail = Thumbnail(TiffBuilder.Gray(256, 256));

        Assert.NotNull(thumbnail);
        Assert.Equal(SKColorType.Gray8, thumbnail.ColorType);
        Assert.True(Math.Max(thumbnail.Width, thumbnail.Height) <= 32);
    }

    [Fact]
    public void SmallRgb_ThumbnailsThroughRgba()
    {
        using var thumbnail = Thumbnail(TiffBuilder.Rgb(256, 256));

        Assert.NotNull(thumbnail);
        Assert.Equal(SKColorType.Rgba8888, thumbnail.ColorType);
        Assert.True(Math.Max(thumbnail.Width, thumbnail.Height) <= 32);
    }

    [Fact]
    public void LargeGray_ThumbnailsByStreaming()
    {
        using var thumbnail = Thumbnail(TiffBuilder.Gray(OverRgbaCeiling, OverRgbaCeiling));

        Assert.NotNull(thumbnail);
        Assert.Equal(SKColorType.Gray8, thumbnail.ColorType);
    }

    [Fact]
    public void FloatGray_ThumbnailsAtItsOwnLayout()
    {
        using var thumbnail = Thumbnail(TiffBuilder.FloatGray(64, 64));

        Assert.NotNull(thumbnail);
        Assert.Equal(SKColorType.Rgba8888, thumbnail.ColorType);
        Assert.True(Math.Max(thumbnail.Width, thumbnail.Height) <= 32);

        // 0x3F3F3F3F is about 0.75, clamped and scaled to eight bits.
        Assert.InRange(thumbnail.GetPixel(0, 0).Red, 185, 195);
    }

    #endregion

    #region Fingerprints

    private static void AssertRgba(Decoded decoded)
    {
        using (decoded)
        {
            var image = Assert.IsType<RasterContent>(decoded.Content).Image;

            Assert.Equal(SKColorType.Rgba8888, image.ColorType);
            Assert.Equal(SKAlphaType.Premul, image.AlphaType);
        }
    }

    private static void AssertGrayRegion(Decoded decoded)
    {
        using (decoded)
        {
            var image = Assert.IsType<RasterContent>(decoded.Content).Image;

            Assert.Equal(SKColorType.Gray8, image.ColorType);
        }
    }

    private static void AssertStreamed(Decoded decoded)
    {
        using (decoded)
        {
            var large = Assert.IsType<RasterLargeContent>(decoded.Content);

            Assert.True(large.HasTiles);
            Assert.Equal(SKColorType.Gray8, large.PreviewImage!.ColorType);
        }
    }

    private static void AssertSceneReferred(Decoded decoded)
    {
        using (decoded)
            Assert.IsType<HdrRasterContent>(decoded.Content);
    }

    #endregion

    #region Decoding

    /// <summary>What a decode produced, holding the file and composite open until checked.</summary>
    private sealed class Decoded(TempFile file, Composite composite, ICompositeContent? content) : IDisposable
    {
        public ICompositeContent? Content => content;

        public void Dispose()
        {
            composite.Dispose();
            file.Dispose();
        }
    }

    private static Decoded DecodeImage(TiffBuilder.Page page)
    {
        var (file, composite) = Decode(TiffBuilder.Build(page));
        return new Decoded(file, composite, composite.Content);
    }

    /// <summary>The same page twice makes a document; the first is decoded along with it.</summary>
    private static Decoded DecodeFirstPage(TiffBuilder.Page page)
    {
        var (file, composite) = Decode(TiffBuilder.Build(page, page));
        var document = Assert.IsType<VariantRasterContent>(composite.Content);

        Assert.Equal(2, document.Variants.Count);

        return new Decoded(file, composite, document.Active);
    }

    private static (TempFile File, Composite Composite) Decode(byte[] tiff)
    {
        Assert.SkipUnless(TiffNativeReady.Value, NotBuilt);

        var file = new TempFile(tiff);
        var composite = new Composite(new FileInfo(file.Path));

        try
        {
            new TiffDecoder().DecodeAsync(composite, TestContext.Current.CancellationToken).GetAwaiter().GetResult();
        }
        catch
        {
            composite.Dispose();
            file.Dispose();
            throw;
        }

        return (file, composite);
    }

    private static SKBitmap? Thumbnail(TiffBuilder.Page page)
    {
        Assert.SkipUnless(TiffNativeReady.Value, NotBuilt);

        using var file = new TempFile(TiffBuilder.Build(page));
        return new TiffDecoder().DecodeThumbnail(file.Path, 32, TestContext.Current.CancellationToken);
    }

    #endregion
}
