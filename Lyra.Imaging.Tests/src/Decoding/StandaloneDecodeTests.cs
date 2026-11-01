using System.Buffers.Binary;
using Lyra.Common;
using Lyra.Imaging.Content;
using Lyra.Imaging.Content.Status;
using Lyra.Imaging.Decoding.Support;
using Lyra.Imaging.Detection;
using Lyra.Imaging.Tests.Support;
using SkiaSharp;
using Xunit;

namespace Lyra.Imaging.Tests.Decoding;

/// <summary>
/// Images decoded outside the loader: recognized by their own bytes and decoded by the decoder a
/// file of that format gets, without any path being opened for bytes held in memory.
/// </summary>
public class StandaloneDecodeTests
{
    private const string ModelPath = "/nowhere/model.glb";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>What a decoded image showed, read out before anything is asserted.</summary>
    private readonly record struct Shown(string? Decoder, int Width, int Height, uint TopLeft, uint TopRight);

    private static Shown Decode(byte[] bytes, string? name = null, string? mimeType = null)
    {
        using var composite = StandaloneDecode.FromBytes(bytes, ModelPath, mimeType, name, Ct);
        if (composite.Content is not RasterContent raster)
            return new Shown($"{composite.DecoderName} gave {composite.Content?.GetType().Name}", 0, 0, 0, 0);

        using var bitmap = SKBitmap.FromImage(raster.Image);
        return new Shown(composite.DecoderName, bitmap.Width, bitmap.Height, (uint)bitmap.GetPixel(0, 0), (uint)bitmap.GetPixel(bitmap.Width - 1, 0));
    }

    private static byte[] Encode(SKEncodedImageFormat format, int width = 8, int height = 4)
    {
        using var bitmap = new SKBitmap(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Unpremul));
        for (var y = 0; y < height; y++)
        for (var x = 0; x < width; x++)
            bitmap.SetPixel(x, y, x < width / 2 ? SKColors.Red : SKColors.Blue);

        using var data = bitmap.Encode(format, 100);
        return data.ToArray();
    }

    [Fact]
    public void APngDecodesFromMemory() =>
        Assert.Equal(new Shown("PngDecoder", 8, 4, 0xFFFF0000, 0xFF0000FF), Decode(Encode(SKEncodedImageFormat.Png)));

    [Fact]
    public void AJpegsOrientationIsAppliedAsItWouldBeOnItsOwn()
    {
        var path = ExifJpegBuilder.Write(orientation: 6);
        byte[] jpeg;
        try
        {
            jpeg = File.ReadAllBytes(path);
        }
        finally
        {
            File.Delete(path);
        }

        var shown = Decode(jpeg);

        Assert.Equal("SkiaDecoder", shown.Decoder);
        Assert.Equal((ExifJpegBuilder.Height, ExifJpegBuilder.Width), (shown.Width, shown.Height));
    }

    [Fact]
    public void TheBytesDecideTheFormatOverTheNameAndMimeType()
    {
        var png = Encode(SKEncodedImageFormat.Png);

        Assert.Equal("PngDecoder", Decode(png, name: "photo.jpg", mimeType: "image/jpeg").Decoder);
    }

    [Fact]
    public void WebpDecodes()
    {
        byte[] webp;
        using (var bitmap = new SKBitmap(new SKImageInfo(6, 6, SKColorType.Rgba8888, SKAlphaType.Unpremul)))
        {
            bitmap.Erase(SKColors.Lime);
            using var pixmap = bitmap.PeekPixels();
            using var data = pixmap.Encode(new SKWebpEncoderOptions(SKWebpEncoderCompression.Lossless, 100))!;
            webp = data.ToArray();
        }

        Assert.Equal(new Shown("WebpDecoder", 6, 6, 0xFF00FF00, 0xFF00FF00), Decode(webp));
    }

    [Fact]
    public void ATgaIsKnownByItsNameOrItsShape()
    {
        var tga = Tga(4, 2);

        Assert.Equal(ImageFormatType.Tga, ImageSignature.Detect(tga, null, @"C:\textures\wood.TGA"));
        Assert.Equal(ImageFormatType.Tga, ImageSignature.Detect(tga, "image/x-tga", null));
        Assert.Equal(ImageFormatType.Tga, ImageSignature.Detect(tga, null, null));

        Assert.Equal(new Shown("TgaDecoder", 4, 2, 0xFFFF0000, 0xFF0000FF), Decode(tga, name: "wood.tga"));
    }

    [Fact]
    public void AnUncompressedDdsDecodes()
    {
        var dds = Dds(4, 2);

        Assert.Equal(new Shown("DdsDecoder", 4, 2, 0xFFFF0000, 0xFF0000FF), Decode(dds));
    }

    [Fact]
    public void AnUncompressedKtxDecodes()
    {
        var ktx = Ktx1(4, 2);

        Assert.Equal(new Shown("KtxDecoder", 4, 2, 0xFFFF0000, 0xFF0000FF), Decode(ktx));
    }

    [Theory]
    [InlineData(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }, ImageFormatType.Png)]
    [InlineData(new byte[] { 0xFF, 0xD8, 0xFF, 0xE0 }, ImageFormatType.Jpeg)]
    [InlineData(new byte[] { 0x47, 0x49, 0x46, 0x38, 0x39, 0x61 }, ImageFormatType.Gif)]
    [InlineData(new byte[] { 0x49, 0x49, 0x2A, 0x00 }, ImageFormatType.Tiff)]
    [InlineData(new byte[] { 0x38, 0x42, 0x50, 0x53 }, ImageFormatType.Psd)]
    [InlineData(new byte[] { 0x76, 0x2F, 0x31, 0x01 }, ImageFormatType.Exr)]
    [InlineData(new byte[] { 0, 0, 0, 0x1C, 0x66, 0x74, 0x79, 0x70, 0x61, 0x76, 0x69, 0x66 }, ImageFormatType.Heif)]
    [InlineData(new byte[] { 0xAB, 0x4B, 0x54, 0x58, 0x20, 0x32, 0x30, 0xBB }, ImageFormatType.Ktx)]
    public void SignaturesAreRecognised(byte[] head, ImageFormatType format) => Assert.Equal(format, ImageSignature.Detect(head, "image/png", "a.png"));

    [Fact]
    public void FormatsThatNeedAPathAreRefusedRatherThanReadFromMemory()
    {
        byte[] tiff = [.. "II*\0"u8, .. new byte[64]];

        var failure = Assert.Throws<LoadFailureException>(() => StandaloneDecode.FromBytes(tiff, ModelPath, null, null, Ct));

        Assert.Equal(LoadFailureKind.DecodeFailed, failure.Kind);
        Assert.Contains("cannot be shown yet", failure.Message);
    }

    [Fact]
    public void BytesOfNoKnownFormatAreRefused()
    {
        byte[] junk = [0x00, 0x13, 0x37, 0x42, 0x99, 0x00, 0x00, 0x00];

        var failure = Assert.Throws<LoadFailureException>(() => StandaloneDecode.FromBytes(junk, ModelPath, null, null, Ct));

        Assert.Contains("not recognised", failure.Message);
    }

    [Fact]
    public void ADamagedImageFailsWithItsDecodersReason()
    {
        byte[] png = [.. Encode(SKEncodedImageFormat.Png)[..16]];

        Assert.Equal(LoadFailureKind.DecodeFailed, Assert.Throws<LoadFailureException>(() => StandaloneDecode.FromBytes(png, ModelPath, null, null, Ct)).Kind);
    }

    /// <summary>Uncompressed 32-bit BGRA, red on the left half and blue on the right, top row first.</summary>
    private static byte[] Tga(int width, int height)
    {
        var tga = new byte[18 + width * height * 4];
        tga[2] = 2;
        BinaryPrimitives.WriteUInt16LittleEndian(tga.AsSpan(12), (ushort)width);
        BinaryPrimitives.WriteUInt16LittleEndian(tga.AsSpan(14), (ushort)height);
        tga[16] = 32;
        tga[17] = 0x28; // 8 alpha bits, top-left origin

        FillBgra(tga.AsSpan(18), width, height);
        return tga;
    }

    private static byte[] Dds(int width, int height)
    {
        var dds = new byte[4 + 124 + width * height * 4];
        var span = dds.AsSpan();

        "DDS "u8.CopyTo(span);
        var header = span[4..];
        BinaryPrimitives.WriteUInt32LittleEndian(header, 124);
        BinaryPrimitives.WriteUInt32LittleEndian(header[4..], 0x100F); // caps, height, width, pitch, pixel format
        BinaryPrimitives.WriteInt32LittleEndian(header[8..], height);
        BinaryPrimitives.WriteInt32LittleEndian(header[12..], width);
        BinaryPrimitives.WriteInt32LittleEndian(header[16..], width * 4);

        var pixelFormat = header[72..];
        BinaryPrimitives.WriteUInt32LittleEndian(pixelFormat, 32);
        BinaryPrimitives.WriteUInt32LittleEndian(pixelFormat[4..], 0x41); // RGB with alpha
        BinaryPrimitives.WriteUInt32LittleEndian(pixelFormat[12..], 32);
        BinaryPrimitives.WriteUInt32LittleEndian(pixelFormat[16..], 0x00FF0000);
        BinaryPrimitives.WriteUInt32LittleEndian(pixelFormat[20..], 0x0000FF00);
        BinaryPrimitives.WriteUInt32LittleEndian(pixelFormat[24..], 0x000000FF);
        BinaryPrimitives.WriteUInt32LittleEndian(pixelFormat[28..], 0xFF000000);
        BinaryPrimitives.WriteUInt32LittleEndian(header[104..], 0x1000); // texture

        FillBgra(span[128..], width, height);
        return dds;
    }

    /// <summary>KTX 1.1, GL_RGBA8 bytes, one level, with the orientation key saying the rows run top down.</summary>
    private static byte[] Ktx1(int width, int height)
    {
        var orientation = "KTXorientation\0S=r,T=d\0"u8.ToArray();
        var keyValueLength = 4 + orientation.Length;
        var paddedKeyValues = (keyValueLength + 3) & ~3;
        var pixels = width * height * 4;

        var ktx = new byte[64 + paddedKeyValues + 4 + pixels];
        var span = ktx.AsSpan();

        ((ReadOnlySpan<byte>)[0xAB, 0x4B, 0x54, 0x58, 0x20, 0x31, 0x31, 0xBB, 0x0D, 0x0A, 0x1A, 0x0A]).CopyTo(span);
        uint[] fields = [0x04030201, 0x1401, 1, 0x1908, 0x8058, 0x1908, (uint)width, (uint)height, 0, 0, 1, 1, (uint)paddedKeyValues];
        for (var i = 0; i < fields.Length; i++)
            BinaryPrimitives.WriteUInt32LittleEndian(span[(12 + i * 4)..], fields[i]);

        BinaryPrimitives.WriteUInt32LittleEndian(span[64..], (uint)orientation.Length);
        orientation.CopyTo(span[68..]);

        var image = span[(64 + paddedKeyValues)..];
        BinaryPrimitives.WriteUInt32LittleEndian(image, (uint)pixels);

        var rgba = image[4..];
        FillBgra(rgba, width, height);
        for (var i = 0; i < pixels; i += 4)
            (rgba[i], rgba[i + 2]) = (rgba[i + 2], rgba[i]);

        return ktx;
    }

    private static void FillBgra(Span<byte> pixels, int width, int height)
    {
        for (var y = 0; y < height; y++)
        for (var x = 0; x < width; x++)
        {
            var at = (y * width + x) * 4;
            var red = x < width / 2;
            pixels[at] = red ? (byte)0 : (byte)255;
            pixels[at + 1] = 0;
            pixels[at + 2] = red ? (byte)255 : (byte)0;
            pixels[at + 3] = 255;
        }
    }
}