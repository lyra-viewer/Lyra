using Lyra.Imaging.Content;
using Lyra.Imaging.Decoding.Decoders;
using Lyra.Imaging.Tests.Support;
using SkiaSharp;
using Xunit;

namespace Lyra.Imaging.Tests.Decoding;

/// <summary>
/// What a file Skia cannot fully read becomes: a load that fails with a reason, or - when Skia
/// gets part of the picture out - that part, flagged as incomplete.
/// </summary>
public class SkiaDecoderFailureTests
{
    private static readonly byte[] Jpeg = Encode(SKEncodedImageFormat.Jpeg);
    private static readonly byte[] Png = Encode(SKEncodedImageFormat.Png);

    [Fact]
    public void AWholeImage_HasNoWarning()
    {
        WithDecoded(Jpeg, composite =>
        {
            Assert.IsType<RasterContent>(composite.Content);
            Assert.Null(composite.Warning);
        });
    }

    [Fact]
    public void AJpegMissingOnlyItsEndMarker_IsShownWhole()
    {
        WithDecoded(Jpeg[..^2], composite =>
        {
            var raster = Assert.IsType<RasterContent>(composite.Content);
            Assert.Null(composite.Warning);

            // Without the repair the last block row is lost, and shows as a transparent strip.
            using var bitmap = SKBitmap.FromImage(raster.Image);
            Assert.Equal(255, bitmap.GetPixel(32, bitmap.Height - 1).Alpha);
        });
    }

    [Theory]
    [InlineData(3)]
    [InlineData(12)]
    [InlineData(200)]
    public void AJpegCutInsideItsPixels_IsStillFlagged(int cut)
    {
        WithDecoded(Jpeg[..^cut], composite =>
        {
            Assert.IsType<RasterContent>(composite.Content);
            Assert.Equal(LoadWarning.PartiallyDecoded("The file ends before the image does"), composite.Warning);
        });
    }

    [Fact]
    public void AnEmptyFile_FailsSayingSo() => Assert.Equal("The file is empty", FailureOf([]).Detail);

    [Fact]
    public void BytesThatAreNoImage_FailSayingSo() =>
        Assert.Equal("The file is not an image, or uses a variant of its format that cannot be decoded", FailureOf([.. Enumerable.Range(0, 200).Select(i => (byte)(i * 37))]).Detail);

    [Fact]
    public void AFileCutInsideItsHeader_FailsSayingSo() => Assert.Equal("The file ends before the image header does", FailureOf(Jpeg[..20]).Detail);

    [Fact]
    public void AFileCutInsideItsPixels_IsShownAndFlagged()
    {
        WithDecoded(Png[..(Png.Length / 2)], composite =>
        {
            Assert.IsType<RasterContent>(composite.Content);
            Assert.Equal(LoadWarning.PartiallyDecoded("The file ends before the image does"), composite.Warning);
        });
    }

    [Fact]
    public void CorruptPixelData_IsShownAsFarAsItGoes_AndFlagged()
    {
        var png = Png.ToArray();
        png[png.Length / 2] ^= 0x55;

        WithDecoded(png, composite =>
        {
            Assert.IsType<RasterContent>(composite.Content);
            Assert.Equal(LoadWarning.PartiallyDecoded("The image data is corrupt partway through"), composite.Warning);
        });
    }

    [Fact]
    public void AFailedLoad_IsADecodeFailure()
    {
        Assert.Equal(LoadFailureKind.DecodeFailed, FailureOf([1, 2, 3]).Kind);
    }

    private static byte[] Encode(SKEncodedImageFormat format)
    {
        using var bitmap = new SKBitmap(64, 48);

        for (var y = 0; y < 48; y++)
        for (var x = 0; x < 64; x++)
            bitmap.SetPixel(x, y, new SKColor((byte)(x * 4), (byte)(y * 5), 128));

        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(format, 90);
        return data.ToArray();
    }

    private static LoadFailure FailureOf(byte[] file)
    {
        using var temp = new TempFile(file);
        using var composite = new Composite(new FileInfo(temp.Path));

        var thrown = Assert.ThrowsAny<Exception>(() => new SkiaDecoder().DecodeAsync(composite, CancellationToken.None).GetAwaiter().GetResult());
        Assert.Null(composite.Content);

        return LoadFailure.From(thrown, temp.Path);
    }

    private static void WithDecoded(byte[] file, Action<Composite> assert)
    {
        using var temp = new TempFile(file);
        using var composite = new Composite(new FileInfo(temp.Path));

        new SkiaDecoder().DecodeAsync(composite, CancellationToken.None).GetAwaiter().GetResult();

        assert(composite);
    }
}
