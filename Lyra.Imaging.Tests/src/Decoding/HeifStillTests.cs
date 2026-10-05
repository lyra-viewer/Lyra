using System.Runtime.InteropServices;
using Lyra.Imaging.Content;
using Lyra.Imaging.Decoding.Decoders;
using Lyra.Imaging.Interop;
using Lyra.Imaging.Tests.Support;
using Lyra.ManagedCodecs.Tests.Heif;
using SkiaSharp;
using Xunit;

namespace Lyra.Imaging.Tests.Decoding;

/// <summary>
/// HEIF and AVIF still images through libheif, which comes from the system; these skip where it
/// cannot be found.
/// </summary>
public class HeifStillTests
{
    private const uint Red = 0xFFFF0000, Blue = 0xFF0000FF;

    [Fact]
    public void AnAvifTaggedDisplayP3_IsTaggedSo()
    {
        WithDecoded(HeifStillFixtures.AvifDisplayP3, composite =>
        {
            var raster = Assert.IsType<RasterContent>(composite.Content);

            Assert.Equal((64, 48), (raster.Image.Width, raster.Image.Height));
            Assert.True(SKColorSpace.Equal(raster.Image.ColorSpace, SKColorSpace.CreateRgb(SKColorSpaceTransferFn.Srgb, SKColorSpaceXyz.DisplayP3)));

            // Lossless, and the pixels stay in the file's own space.
            Assert.Equal(Red, (uint)PixelOf(raster, 10, 10));
            Assert.Equal(Blue, (uint)PixelOf(raster, 50, 10));

            var facts = Facts(composite);
            Assert.Equal("AV1", facts["Codec"]);
            Assert.Equal("Yes", facts["Has Alpha"]);
            Assert.Equal("No", facts["Depth Map"]);
            Assert.False(facts.ContainsKey("Thumbnails"));
            Assert.False(facts.ContainsKey("Top-level Images"));
        });
    }

    [Fact]
    public void AHeicWithAnIccProfile_IsTaggedWithIt_AndCroppedToItsSize()
    {
        WithDecoded(HeifStillFixtures.HeicAdobeRgb, composite =>
        {
            var raster = Assert.IsType<RasterContent>(composite.Content);

            Assert.Equal((64, 48), (raster.Image.Width, raster.Image.Height));
            Assert.Equal("HEVC", Facts(composite)["Codec"]);

            var colorSpace = raster.Image.ColorSpace;
            Assert.NotNull(colorSpace);
            Assert.False(colorSpace.IsSrgb);

            var gamut = colorSpace.ToColorSpaceXyz().Values;
            var adobe = SKColorSpaceXyz.AdobeRgb.Values;
            Assert.All(gamut.Zip(adobe), pair => Assert.InRange(pair.First, pair.Second - 0.01f, pair.Second + 0.01f));

            var red = PixelOf(raster, 10, 10);
            Assert.True(red.Red > 230 && red.Green < 30 && red.Blue < 30, $"expected red, got {red}");
        });
    }

    [Fact]
    public void AThumbnailExifAndAlpha_AreRead()
    {
        WithDecoded(HeifStillFixtures.HeicThumbnailAndExif, composite =>
        {
            var raster = Assert.IsType<RasterContent>(composite.Content);

            Assert.Equal(0, PixelOf(raster, 5, 5).Alpha);
            Assert.True(PixelOf(raster, 50, 5).Green > 230);

            var facts = Facts(composite);
            Assert.Equal("1", facts["Thumbnails"]);
            Assert.Equal("Yes", facts["Has Alpha"]);

            Assert.Equal("Lyra", composite.ExifInfo?.Make);
            Assert.Equal("Fixture", composite.ExifInfo?.Model);
        });
    }

    [Fact]
    public void TheExifBlock_IsReadFromItsTiffHeaderOn()
    {
        WithFile(HeifStillFixtures.HeicThumbnailAndExif, path =>
        {
            using var stream = File.OpenRead(path);
            using var file = HeifFile.Open(stream);
            using var image = file.PrimaryImage();

            // HEIF stores a 4-byte offset and "Exif\0\0" before the TIFF header; only the TIFF part is returned.
            var exif = image.Exif();
            Assert.NotNull(exif);
            Assert.Equal("MM\0*"u8.ToArray(), exif[..4]);
            Assert.Equal(64, exif.Length);
        });
    }

    [Fact]
    public void LibheifReadsOnlyWhatItNeeds()
    {
        if (!LibheifReady.Value)
            Assert.Skip("libheif not available on this machine.");

        var padding = new byte[1024 * 1024];
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(padding, (uint)padding.Length);
        "free"u8.CopyTo(padding.AsSpan(4));

        var stream = new CountingStream([.. Convert.FromBase64String(HeifStillFixtures.AvifDisplayP3), .. padding]);

        using (var file = HeifFile.Open(stream))
        using (var image = file.PrimaryImage())
        using (image.DecodeRgba(colorSpace: null, CancellationToken.None)) { }

        Assert.True(stream.BytesRead < 64 * 1024, $"read {stream.BytesRead} bytes of {stream.Length}");
    }

    [Fact]
    public void TheFileLength_IsAskedForOnce()
    {
        if (!LibheifReady.Value)
            Assert.Skip("libheif not available on this machine.");

        var stream = new CountingStream(Convert.FromBase64String(HeifStillFixtures.HeicThumbnailAndExif));

        using (var file = HeifFile.Open(stream))
        using (var image = file.PrimaryImage())
        using (image.DecodeRgba(colorSpace: null, CancellationToken.None)) { }

        Assert.Equal(1, stream.LengthQueries);
    }

    [Fact]
    public void ACancelledRead_IsACancellation()
    {
        if (!LibheifReady.Value)
            Assert.Skip("libheif not available on this machine.");

        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();

        using var stream = new MemoryStream(Convert.FromBase64String(HeifStillFixtures.AvifDisplayP3));
        Assert.Throws<OperationCanceledException>(() => HeifFile.Open(stream, cancelled.Token));
    }

    [Theory]
    [InlineData(16, 16, 12)]
    [InlineData(32, 32, 24)]
    public void TheThumbnail_UsesTheEmbeddedOneWhenItIsLargeEnough(int maxDimension, int width, int height)
    {
        WithFile(HeifStillFixtures.HeicThumbnailAndExif, path =>
        {
            using var thumbnail = new HeifDecoder().DecodeThumbnail(path, maxDimension, CancellationToken.None);

            Assert.NotNull(thumbnail);
            Assert.Equal((width, height), (thumbnail.Width, thumbnail.Height));
            Assert.True(thumbnail.GetPixel(width - 2, height / 2).Green > 200);
        });
    }

    [Fact]
    public void AThumbnailWithoutAnEmbeddedOne_IsScaledFromTheImage()
    {
        WithFile(HeifStillFixtures.AvifDisplayP3, path =>
        {
            using var thumbnail = new HeifDecoder().DecodeThumbnail(path, 32, CancellationToken.None);

            Assert.NotNull(thumbnail);
            Assert.Equal((32, 24), (thumbnail.Width, thumbnail.Height));
            Assert.Equal(Red, (uint)thumbnail.GetPixel(4, 4));
        });
    }

    [Fact]
    public void FurtherTopLevelImages_AreCounted_AndThePrimaryShown()
    {
        WithDecoded(HeifStillFixtures.AvifTwoImages, composite =>
        {
            Assert.Equal("2", Facts(composite)["Top-level Images"]);
            Assert.Equal(Red, (uint)PixelOf(Assert.IsType<RasterContent>(composite.Content), 10, 10));
        });
    }

    [Fact]
    public void ACutFile_FailsWithLibheifsReason()
    {
        var failure = Failure(Convert.FromBase64String(HeifStillFixtures.AvifDisplayP3)[..300]);

        Assert.Equal(LoadFailureKind.DecodeFailed, failure.Kind);
        Assert.StartsWith("Could not read the file: ", failure.Detail);
    }

    [Fact]
    public void AFileLibheifRejects_FailsWithLibheifsReason()
    {
        // A sequence track whose codec does not match its sample entry makes libheif refuse the whole file.
        var file = Convert.FromBase64String(HeifSequenceFixtures.Avif);
        var stsd = file.AsSpan().IndexOf("stsd"u8);
        "zzzz"u8.CopyTo(file.AsSpan(stsd + file.AsSpan(stsd).IndexOf("av01"u8)));

        var failure = Failure(file);

        Assert.Equal(LoadFailureKind.DecodeFailed, failure.Kind);
        Assert.Contains("track", failure.Detail);
    }

    /// <summary>The load failure the decoder's exception becomes, as the loader turns it into one.</summary>
    private static LoadFailure Failure(byte[] file)
    {
        if (!LibheifReady.Value)
            Assert.Skip("libheif not available on this machine.");

        using var temp = new TempFile(file);
        using var composite = new Composite(new FileInfo(temp.Path));

        var thrown = Assert.ThrowsAny<Exception>(() => new HeifDecoder().DecodeAsync(composite, CancellationToken.None).GetAwaiter().GetResult());
        Assert.Null(composite.Content);

        return LoadFailure.From(thrown, temp.Path);
    }

    private static readonly string[] LibheifCandidates = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
        ? ["libheif.dll", "heif.dll"]
        : RuntimeInformation.IsOSPlatform(OSPlatform.Linux)
            ? ["libheif.so.1", "libheif.so"]
            : ["/opt/homebrew/opt/libheif/lib/libheif.dylib", "/usr/local/opt/libheif/lib/libheif.dylib", "libheif.dylib"];

    private static readonly Lazy<bool> LibheifReady = new(() => NativeWrapper.TryLoadSystem("libheif", LibheifCandidates, typeof(HeifDecoder).Assembly));

    private static void WithFile(string base64, Action<string> act)
    {
        if (!LibheifReady.Value)
            Assert.Skip("libheif not available on this machine.");

        using var temp = new TempFile(Convert.FromBase64String(base64));
        act(temp.Path);
    }

    private static void WithDecoded(string base64, Action<Composite> assert) => WithDecoded(Convert.FromBase64String(base64), assert);

    private static void WithDecoded(byte[] file, Action<Composite> assert)
    {
        if (!LibheifReady.Value)
            Assert.Skip("libheif not available on this machine.");

        using var temp = new TempFile(file);
        using var composite = new Composite(new FileInfo(temp.Path));

        new HeifDecoder().DecodeAsync(composite, CancellationToken.None).GetAwaiter().GetResult();

        assert(composite);
    }

    private static Dictionary<string, string> Facts(Composite composite) => composite.FormatSpecificSnapshot().ToDictionary(p => p.Key, p => p.Value);

    private static SKColor PixelOf(RasterContent raster, int x, int y)
    {
        using var bitmap = SKBitmap.FromImage(raster.Image);
        return bitmap.GetPixel(x, y);
    }
}