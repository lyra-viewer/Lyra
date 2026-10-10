using System.Buffers.Binary;
using System.Text;
using Lyra.Common;
using Lyra.Imaging.Content;
using Lyra.Imaging.Decoding.Decoders;
using Lyra.Imaging.Loading;
using Lyra.Imaging.Tests.Support;
using SkiaSharp;
using Xunit;

namespace Lyra.Imaging.Tests.Decoding;

/// <summary>High-Throughput JPEG 2000 (ISO/IEC 15444-15), which OpenJPEG reads like any other JPEG 2000.</summary>
public class Htj2kTests
{
    private const uint Red = 0xFFFF0000, Blue = 0xFF0000FF;

    // 16x12 RGB, red on the left half and blue on the right, coded reversibly with HT code blocks
    // (cblksty 0x40): ojph_compress -reversible true, OpenJPH 0.32.
    private const string Codestream = "/0//UQAvQAAAAAAQAAAADAAAAAAAAAAAAAAAEAAAAAwAAAAAAAAAAAADBwEBBwEBBwEB/1AACAACAAAAA/9SAAwAAgABAQUEBEAB/1wAEyBQWFhYWFhYWFhYWFhYUFBQ" +
                                      "/2QAFwABT3BlbkpQSCBWZXIgMC4zMi4wLv+QAAoAAAAAAJMAAf+TwBKAgQEHdADAEoDsAId0AMASgJABh3QAAAAAAMAJQN4Bh3QAwAlA4QGHdAAAwAqABQsMDQBhlADA" +
                                      "CoAGDQ4PAGGUAADACtAdHe4d/gjgLjDD0ngAwArQHh7/Hv4I4C4ww9J4AADAFaD9fr/f77wBTlOBAngAwBWgfr/f7/e8AU5TgQJ4AP/Z";

    private static readonly Lazy<bool> NativeJ2KReady = new(() => NativeWrapper.TryLoad("libj2k_native", typeof(J2KDecoder).Assembly));

    [Theory]
    [InlineData(".jph", ImageFormatType.Jp2)]
    [InlineData(".jhc", ImageFormatType.J2k)]
    public void TheHtExtensions_AreJpeg2000(string extension, ImageFormatType format)
    {
        Assert.Equal(format, ImageFormat.GetImageFormat(extension));
        Assert.IsType<J2KDecoder>(DecoderManager.GetDecoder(format));
    }

    [Fact]
    public void AnHtCodestream_IsDecoded() => AssertDecodes(Convert.FromBase64String(Codestream), ".jhc");

    [Fact]
    public void AnHtCodestreamInAJphContainer_IsDecoded() => AssertDecodes(Jph(Convert.FromBase64String(Codestream)), ".jph");

    private static void AssertDecodes(byte[] file, string extension)
    {
        if (!NativeJ2KReady.Value)
            Assert.Skip("libj2k_native not available (native wrappers not built for this platform).");

        var path = Path.Combine(Path.GetTempPath(), $"lyra-htj2k-{Guid.NewGuid():N}{extension}");
        File.WriteAllBytes(path, file);

        try
        {
            using var composite = new Composite(new FileInfo(path));
            DecoderManager.GetDecoder(ImageFormat.GetImageFormat(extension)).DecodeAsync(composite, CancellationToken.None).GetAwaiter().GetResult();

            var raster = Assert.IsType<RasterContent>(composite.Content);
            using var bitmap = SKBitmap.FromImage(raster.Image);

            Assert.Equal((16, 12), (bitmap.Width, bitmap.Height));
            Assert.Equal(Red, (uint)bitmap.GetPixel(2, 6));
            Assert.Equal(Blue, (uint)bitmap.GetPixel(13, 6));
        }
        finally
        {
            File.Delete(path);
        }
    }

    /// <summary>The codestream in the JPH file format: JP2's boxes under the "jph " brand, sRGB.</summary>
    private static byte[] Jph(byte[] codestream)
    {
        byte[] ihdr = [.. U32(12), .. U32(16), .. U16(3), 7, 7, 0, 0];
        byte[] colr = [1, 0, 0, .. U32(16)];

        return
        [
            .. Box("jP  ", [0x0D, 0x0A, 0x87, 0x0A]),
            .. Box("ftyp", [.. Encoding.ASCII.GetBytes("jph "), .. U32(0), .. Encoding.ASCII.GetBytes("jph ")]),
            .. Box("jp2h", [.. Box("ihdr", ihdr), .. Box("colr", colr)]),
            .. Box("jp2c", codestream),
        ];
    }

    private static byte[] Box(string type, byte[] body) => [.. U32((uint)(8 + body.Length)), .. Encoding.ASCII.GetBytes(type), .. body];

    private static byte[] U32(uint value)
    {
        var bytes = new byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(bytes, value);
        return bytes;
    }

    private static byte[] U16(ushort value)
    {
        var bytes = new byte[2];
        BinaryPrimitives.WriteUInt16BigEndian(bytes, value);
        return bytes;
    }
}