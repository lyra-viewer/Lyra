using Lyra.ManagedCodecs.Raster.Qoi;
using Xunit;

namespace Lyra.ManagedCodecs.Tests.Qoi;

public class QoiReaderTests
{
    private const int Width = 64, Height = 16;

    private static (byte[] Rgba, byte[] Qoi) VariedImage()
    {
        var rgba = QoiTestImage.Varied(Width, Height);
        return (rgba, QoiTestImage.Encode(rgba, Width, Height));
    }

    private static (long Decoded, byte[] Rgba) Decode(byte[] qoi)
    {
        var header = QoiReader.ReadHeader(qoi);
        var rgba = new byte[header.PixelCount * 4];
        return (QoiReader.DecodeInto(qoi, header, rgba), rgba);
    }

    [Fact]
    public void ARoundTrip_ThroughTheReferenceEncoder_IsExact()
    {
        var (rgba, qoi) = VariedImage();

        var (decoded, result) = Decode(qoi);

        Assert.Equal(Width * Height, decoded);
        Assert.Equal(rgba, result);
    }

    [Fact]
    public void TheVariedImage_UsesEveryChunk()
    {
        var (_, qoi) = VariedImage();
        var body = qoi.AsSpan(QoiHeader.Size, qoi.Length - QoiHeader.Size - 8);

        Assert.Contains(QoiTestImage.OpRgb, body.ToArray());
        Assert.Contains(QoiTestImage.OpRgba, body.ToArray());

        var tags = new HashSet<int>();
        foreach (var b in body)
            if (b is not (QoiTestImage.OpRgb or QoiTestImage.OpRgba))
                tags.Add(b & 0xC0);

        Assert.Equal([QoiTestImage.OpIndex, QoiTestImage.OpDiff, QoiTestImage.OpLuma, QoiTestImage.OpRun], tags.Order().Select(t => (byte)t));
    }

    [Fact]
    public void AnRgbFile_DecodesOpaque()
    {
        var rgba = QoiTestImage.Varied(Width, Height);
        var qoi = QoiTestImage.Encode(rgba, Width, Height, channels: 3);

        var (_, result) = Decode(qoi);

        for (var i = 0; i < Width * Height; i++)
        {
            Assert.Equal(rgba.AsSpan(i * 4, 3).ToArray(), result.AsSpan(i * 4, 3).ToArray());
            Assert.Equal(255, result[i * 4 + 3]);
        }
    }

    [Fact]
    public void TheHeader_IsReadBigEndian()
    {
        var qoi = QoiTestImage.Encode(new byte[300 * 2 * 4], 300, 2, channels: 3, colorSpace: 1);

        Assert.Equal(new QoiHeader(300, 2, 3, QoiColorSpace.Linear), QoiReader.ReadHeader(qoi));
    }

    [Theory]
    [InlineData(new byte[] { (byte)'q', (byte)'o', (byte)'i', (byte)'f', 0, 0, 0, 1, 0, 0, 0, 1, 4 })]
    [InlineData(new byte[] { (byte)'q', (byte)'o', (byte)'i', (byte)'x', 0, 0, 0, 1, 0, 0, 0, 1, 4, 0 })]
    [InlineData(new byte[] { (byte)'q', (byte)'o', (byte)'i', (byte)'f', 0, 0, 0, 0, 0, 0, 0, 1, 4, 0 })]
    [InlineData(new byte[] { (byte)'q', (byte)'o', (byte)'i', (byte)'f', 0, 0, 0, 1, 0, 0, 0, 1, 5, 0 })]
    [InlineData(new byte[] { (byte)'q', (byte)'o', (byte)'i', (byte)'f', 0, 0, 0, 1, 0, 0, 0, 1, 4, 2 })]
    public void ABadHeader_IsRefused(byte[] data) =>
        Assert.Throws<InvalidDataException>(() => QoiReader.ReadHeader(data));

    [Fact]
    public void AFileCutShort_KeepsWhatItHeld_AndClearsTheRest()
    {
        var (rgba, qoi) = VariedImage();

        var (decoded, result) = Decode(qoi[..(qoi.Length / 2)]);

        Assert.InRange(decoded, 1, Width * Height - 1);
        Assert.Equal(rgba.AsSpan(0, (int)decoded * 4).ToArray(), result.AsSpan(0, (int)decoded * 4).ToArray());
        Assert.All(result.AsSpan((int)decoded * 4).ToArray(), b => Assert.Equal(0, b));
    }

    [Fact]
    public void EveryCutPoint_DecodesAPrefix_WithoutReadingPastTheData()
    {
        var (rgba, qoi) = VariedImage();
        var previous = 0L;

        for (var length = QoiHeader.Size; length < qoi.Length - 8; length++)
        {
            var (decoded, result) = Decode(qoi[..length]);

            Assert.True(decoded >= previous, $"cutting at {length} decoded fewer pixels than cutting earlier");
            Assert.Equal(rgba.AsSpan(0, (int)decoded * 4).ToArray(), result.AsSpan(0, (int)decoded * 4).ToArray());
            previous = decoded;
        }
    }

    [Fact]
    public void AMissingEndMarker_IsNoFault_WhenEveryPixelIsThere()
    {
        var (rgba, qoi) = VariedImage();

        var (decoded, result) = Decode(qoi[..^8]);

        Assert.Equal(Width * Height, decoded);
        Assert.Equal(rgba, result);
    }

    [Fact]
    public void AnImageOverTheLimit_IsRefusedBeforeAnythingIsWritten()
    {
        var header = new QoiHeader(100_000, 100_000, 4, QoiColorSpace.SrgbLinearAlpha);

        Assert.Throws<NotSupportedException>(() => QoiReader.DecodeInto([], header, []));
    }

    [Fact]
    public void AnOutputTooSmall_IsRefused()
    {
        var (_, qoi) = VariedImage();

        Assert.Throws<ArgumentException>(() => QoiReader.DecodeInto(qoi, QoiReader.ReadHeader(qoi), new byte[16]));
    }

    [Fact]
    public void ACancelledDecode_Stops()
    {
        var (_, qoi) = VariedImage();
        var header = QoiReader.ReadHeader(qoi);

        Assert.Throws<OperationCanceledException>(() => QoiReader.DecodeInto(qoi, header, new byte[header.PixelCount * 4], new CancellationToken(canceled: true)));
    }
}
