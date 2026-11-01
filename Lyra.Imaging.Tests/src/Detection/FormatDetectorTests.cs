using Lyra.Common;
using Lyra.Imaging.Content.Status;
using Lyra.Imaging.Detection;
using Lyra.Imaging.Loading;
using Lyra.Imaging.Tests.Support;
using Xunit;

namespace Lyra.Imaging.Tests.Detection;

/// <summary>
/// A file's name decides its format unless its bytes conclusively say it is something the name's
/// decoder cannot read, as with a download named .webp that is really AVIF.
/// </summary>
public sealed class FormatDetectorTests : IDisposable
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);

    private readonly string _folder = Directory.CreateTempSubdirectory("lyra-detect-").FullName;

    public void Dispose()
    {
        try
        {
            Directory.Delete(_folder, recursive: true);
        }
        catch (IOException) { }
    }

    private string Write(string name, byte[] bytes)
    {
        var path = Path.Combine(_folder, name);
        File.WriteAllBytes(path, bytes);
        return path;
    }

    private static readonly byte[] Avif = Convert.FromBase64String(HeifStillFixtures.AvifDisplayP3);
    private static readonly byte[] Png = [0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 13];
    private static readonly byte[] Jpeg = [0xFF, 0xD8, 0xFF, 0xE0, 0, 16];
    private static readonly byte[] Tiff = [(byte)'I', (byte)'I', 42, 0, 8, 0, 0, 0];

    private static byte[] Ftyp(string major, params string[] compatible)
    {
        var brands = compatible.Prepend("\0\0\0\0").Prepend(major).SelectMany(brand => System.Text.Encoding.ASCII.GetBytes(brand)).ToArray();
        byte[] box = [0, 0, 0, (byte)(8 + brands.Length), .. "ftyp"u8, .. brands];
        return [.. box, .. new byte[16]];
    }

    [Theory]
    [InlineData(ImageFormatType.Webp, ImageFormatType.Heif)]
    [InlineData(ImageFormatType.Jpeg, ImageFormatType.Heif)]
    [InlineData(ImageFormatType.Heif, ImageFormatType.Heif)]
    public void AnAvifIsReadAsHeifWhateverItIsCalled(ImageFormatType named, ImageFormatType read) => Assert.Equal(read, FormatDetector.Resolve(named, Avif));

    [Fact]
    public void APngNamedJpegGoesToThePngDecoder() => Assert.Equal(ImageFormatType.Png, FormatDetector.Resolve(ImageFormatType.Jpeg, Png));

    [Theory]
    [InlineData(ImageFormatType.Jfif)]
    [InlineData(ImageFormatType.Jpeg)]
    public void JpegAndJfifAreOneFormat(ImageFormatType named) => Assert.Equal(named, FormatDetector.Resolve(named, Jpeg));

    [Fact]
    public void ARawThatIsTiffInsideStaysARaw()
    {
        Assert.Equal(ImageFormatType.Raw, FormatDetector.Resolve(ImageFormatType.Raw, Tiff));
        Assert.Equal(ImageFormatType.Raw, FormatDetector.Resolve(ImageFormatType.Raw, Jpeg));
    }

    [Fact]
    public void AnIconThatIsAPlainPngStaysAnIcon() => Assert.Equal(ImageFormatType.Ico, FormatDetector.Resolve(ImageFormatType.Ico, Png));

    [Fact]
    public void AnInconclusiveSignatureNeverOverridesTheName()
    {
        Assert.Equal(ImageFormatType.Png, FormatDetector.Resolve(ImageFormatType.Png, "BM\0\0\0\0"u8.ToArray()));
        Assert.Equal(ImageFormatType.Tga, FormatDetector.Resolve(ImageFormatType.Tga, "#?anything"u8.ToArray()));
        Assert.Equal(ImageFormatType.Png, FormatDetector.Resolve(ImageFormatType.Png, [0, 0, 1, 0, 1, 0]));
        Assert.Equal(ImageFormatType.Png, FormatDetector.Resolve(ImageFormatType.Png, []));
    }

    [Fact]
    public void OnlyAHeifBrandMakesAnIsoFileHeif()
    {
        Assert.Equal(new SignatureMatch(ImageFormatType.Heif, true), ImageSignature.Match(Ftyp("mif1", "heic")));
        Assert.Equal(new SignatureMatch(ImageFormatType.Raw, true), ImageSignature.Match(Ftyp("crx ", "isom")));
        Assert.Null(ImageSignature.Match(Ftyp("isom", "iso2", "mp41")));
        Assert.Equal(ImageFormatType.Webp, FormatDetector.Resolve(ImageFormatType.Webp, Ftyp("qt  ")));
    }

    [Fact]
    public void PsdAndPsbAreToldApartByTheirVersion()
    {
        Assert.Equal(ImageFormatType.Psd, ImageSignature.Match([.. "8BPS"u8, 0, 1])?.Format);
        Assert.Equal(ImageFormatType.Psb, ImageSignature.Match([.. "8BPS"u8, 0, 2])?.Format);
        Assert.Equal(ImageFormatType.Psd, FormatDetector.Resolve(ImageFormatType.Psd, [.. "8BPS"u8, 0, 2]));
    }

    [Fact]
    public void AFileIsDetectedFromItsFirstBytes()
    {
        var detection = FormatDetector.Detect(Write("download.webp", Avif));

        Assert.Equal(new FormatDetection(ImageFormatType.Webp, ImageFormatType.Heif), detection);
        Assert.True(detection.IsMisnamed);
    }

    [Fact]
    public void AFileWithANameThatSaysNothingIsIdentifiedOnlyByAConclusiveSignature()
    {
        Assert.Equal(ImageFormatType.Png, FormatDetector.Identify(Write("photo", Png)));
        Assert.Equal(ImageFormatType.Heif, FormatDetector.Identify(Write("download", Avif)));
        Assert.Null(FormatDetector.Identify(Write("notes", "BM is not a bitmap"u8.ToArray())));
        Assert.Null(FormatDetector.Identify(Write("clip", Ftyp("isom", "mp41"))));
        Assert.Null(FormatDetector.Identify(Path.Combine(_folder, "gone")));
    }

    [Fact]
    public void AFileWithNoExtensionSaysSo()
    {
        Assert.Equal(new LoadWarning("No extension", "Read as PNG Image from its contents"), LoadWarning.Misnamed("", "PNG Image"));
        Assert.Equal("Wrong extension", LoadWarning.Misnamed(".jpg_large", "JPEG Image").Message);
    }

    [Fact]
    public void AFileThatCannotBeReadKeepsItsName() => Assert.Equal(new FormatDetection(ImageFormatType.Png, ImageFormatType.Png), FormatDetector.Detect(Path.Combine(_folder, "gone.png")));

    /// <summary>What a load ended as, read out before anything is asserted.</summary>
    private readonly record struct Loaded(CompositeState State, string? Failure, ImageFormatType Format, string? Decoder, float Width, float Height, LoadWarning? Warning);

    private static Loaded Load(string path)
    {
        using var loader = new ImageLoader();

        var composite = loader.GetImage(path);
        Assert.True(SpinWait.SpinUntil(() => composite.State is CompositeState.Complete or CompositeState.Failed, Patience), $"State: {composite.State}");

        return new Loaded(composite.State, composite.Failure?.Detail, composite.ImageFormatType, composite.DecoderName, composite.LogicalWidth, composite.LogicalHeight, composite.Warning);
    }

    [Fact]
    public void AnAvifNamedWebpOpensAndSaysItWasMisnamed()
    {
        if (!Libheif.Ready)
            Assert.Skip("libheif not available on this machine.");

        var loaded = Load(Write("download.webp", Avif));

        Assert.Equal(new Loaded(CompositeState.Complete, null, ImageFormatType.Heif, "HeifDecoder", 64, 48, LoadWarning.Misnamed(".webp", "HEIF Image")), loaded);
    }

    [Fact]
    public void AWebpNamedPngGoesToTheWebpDecoder()
    {
        byte[] webp;
        using (var bitmap = new SkiaSharp.SKBitmap(new SkiaSharp.SKImageInfo(6, 4, SkiaSharp.SKColorType.Rgba8888, SkiaSharp.SKAlphaType.Unpremul)))
        {
            bitmap.Erase(SkiaSharp.SKColors.Teal);
            using var pixmap = bitmap.PeekPixels();
            using var data = pixmap.Encode(new SkiaSharp.SKWebpEncoderOptions(SkiaSharp.SKWebpEncoderCompression.Lossless, 100))!;
            webp = data.ToArray();
        }

        var loaded = Load(Write("photo.png", webp));

        Assert.Equal(new Loaded(CompositeState.Complete, null, ImageFormatType.Webp, "WebpDecoder", 6, 4, LoadWarning.Misnamed(".png", "WebP Image")), loaded);
    }

    [Fact]
    public void AFileWhoseNameIsRightOpensWithoutAWarning()
    {
        var path = MinimalImageBuilder.WriteBmp();
        try
        {
            var loaded = Load(path);

            Assert.Equal((CompositeState.Complete, ImageFormatType.Bmp, (LoadWarning?)null), (loaded.State, loaded.Format, loaded.Warning));
        }
        finally
        {
            File.Delete(path);
        }
    }
}