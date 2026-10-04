using System.Buffers.Binary;
using Lyra.Common;
using Lyra.Imaging.Content;
using Lyra.Imaging.Decoding.Decoders;
using Lyra.Imaging.Loading;
using Lyra.Imaging.Tests.Support;
using SkiaSharp;
using Xunit;

namespace Lyra.Imaging.Tests.Decoding;

public class QoiDecoderTests
{
    private const int Width = 16, Height = 12;

    [Fact]
    public void QoiIsRegistered()
    {
        Assert.Equal(ImageFormatType.Qoi, ImageFormat.GetImageFormat(".qoi"));
        Assert.IsType<QoiDecoder>(DecoderManager.GetDecoder(ImageFormatType.Qoi));
    }

    [Fact]
    public void AQoi_IsShown_WithItsFacts_AndNoMetadata()
    {
        WithDecoded(Qoi(Width, Height, (x, _) => x < 8 ? 0xFFFF0000u : 0x8000FF00u), composite =>
        {
            var raster = Assert.IsType<RasterContent>(composite.Content);
            Assert.Equal((Width, Height), (raster.Image.Width, raster.Image.Height));

            using var bitmap = SKBitmap.FromImage(raster.Image);
            var pixels = new[] { (uint)bitmap.GetPixel(1, 1), (uint)bitmap.GetPixel(12, 1) };
            Assert.Equal([0xFFFF0000u, 0x8000FF00u], pixels);

            var facts = composite.FormatSpecificSnapshot().ToDictionary(p => p.Key, p => p.Value);
            Assert.Equal("QOI", facts["Format"]);
            Assert.Equal("RGBA", facts["Channels"]);
            Assert.Equal("sRGB, linear alpha", facts["Color Space"]);

            // QOI holds no metadata: the panel says there is none, not that reading it failed.
            Assert.Null(composite.ExifInfo);
            Assert.Null(composite.Warning);
        });
    }

    [Fact]
    public void ALinearQoi_IsTaggedLinear()
    {
        WithDecoded(Qoi(Width, Height, (_, _) => 0xFF808080u, channels: 3, colorSpace: 1), composite =>
        {
            var raster = Assert.IsType<RasterContent>(composite.Content);

            Assert.True(raster.Image.ColorSpace.GammaIsLinear);
            Assert.Equal("linear", composite.FormatSpecificSnapshot().Single(p => p.Key == "Color Space").Value);
        });
    }

    [Fact]
    public void AQoiCutShort_IsShownAsFarAsItGoes_WithAWarning()
    {
        var qoi = Qoi(Width, Height, (_, _) => 0xFF0000FFu);

        // Header, then 40 five-byte chunks: forty pixels and no end marker.
        WithDecoded(qoi[..(14 + 40 * 5)], composite =>
        {
            Assert.IsType<RasterContent>(composite.Content);

            var why = $"The file ends after 40 of its {Width * Height} pixels";
            Assert.Equal(LoadWarning.PartiallyDecoded(why), composite.Warning);
        });
    }

    [Fact]
    public void AnImageOverTheLimit_IsTooLarge()
    {
        var header = Qoi(1, 1, (_, _) => 0)[..14];
        BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(4), 100_000);
        BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(8), 100_000);

        var thrown = Assert.Throws<LoadFailureException>(() => Decode(header));

        Assert.Equal(LoadFailureKind.TooLarge, thrown.Kind);
    }

    [Fact]
    public void ABadHeader_FailsWithTheReadersReason()
    {
        var qoi = Qoi(Width, Height, (_, _) => 0);
        qoi[12] = 7;

        var thrown = Assert.Throws<InvalidDataException>(() => Decode(qoi));

        Assert.Equal("QOI: invalid channel count 7.", thrown.Message);
    }

    [Fact]
    public void APngNamedQoi_IsReadForWhatItIs()
    {
        using var source = new SKBitmap(Width, Height);
        source.Erase(SKColors.Blue);
        using var png = source.Encode(SKEncodedImageFormat.Png, 100);

        WithDecoded(png.ToArray(), composite =>
        {
            Assert.NotNull(composite.Content);
            Assert.Equal(nameof(SkiaDecoder), composite.DecoderName);
        });
    }

    [Fact]
    public void TheThumbnail_IsReducedToFit()
    {
        using var file = new TempFile(Qoi(300, 200, (_, _) => 0xFF0000FFu));

        using var thumbnail = new QoiDecoder().DecodeThumbnail(file.Path, 32, CancellationToken.None);

        Assert.NotNull(thumbnail);
        Assert.Equal(32, Math.Max(thumbnail.Width, thumbnail.Height));
    }

    /// <summary>A valid QOI of RGBA chunks only, from colours packed as 0xAARRGGBB.</summary>
    private static byte[] Qoi(int width, int height, Func<int, int, uint> argb, byte channels = 4, byte colorSpace = 0)
    {
        var output = new List<byte>("qoif"u8.ToArray());
        var size = new byte[8];
        BinaryPrimitives.WriteUInt32BigEndian(size, (uint)width);
        BinaryPrimitives.WriteUInt32BigEndian(size.AsSpan(4), (uint)height);
        output.AddRange(size);
        output.AddRange([channels, colorSpace]);

        for (var y = 0; y < height; y++)
        for (var x = 0; x < width; x++)
        {
            var c = argb(x, y);
            output.AddRange([0xFF, (byte)(c >> 16), (byte)(c >> 8), (byte)c, (byte)(c >> 24)]);
        }

        output.AddRange([0, 0, 0, 0, 0, 0, 0, 1]);
        return [.. output];
    }

    private static void Decode(byte[] qoi)
    {
        using var file = new TempFile(qoi);
        using var composite = new Composite(new FileInfo(file.Path));
        new QoiDecoder().DecodeAsync(composite, CancellationToken.None).GetAwaiter().GetResult();
    }

    private static void WithDecoded(byte[] qoi, Action<Composite> assert)
    {
        using var file = new TempFile(qoi);
        using var composite = new Composite(new FileInfo(file.Path));

        new QoiDecoder().DecodeAsync(composite, CancellationToken.None).GetAwaiter().GetResult();

        assert(composite);
    }
}
