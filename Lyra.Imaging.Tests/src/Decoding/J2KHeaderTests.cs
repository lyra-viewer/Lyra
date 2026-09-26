using System.Buffers.Binary;
using System.Diagnostics;
using System.Text;
using Lyra.Imaging.Content;
using Lyra.Imaging.Decoding.Support;
using Xunit;

namespace Lyra.Imaging.Tests.Decoding;

public class J2KHeaderTests
{
    private static readonly byte[] BrokenJpc = Convert.FromHexString(
        "FF4FFF51002F0000000000CB0020009800000000000000000000" +
        "00CB00000098000000000000000000030701010701010701");

    private static byte[] Codestream(uint width, uint height, ushort components, uint xOrigin = 0, uint yOrigin = 0)
    {
        var bytes = new byte[42];
        var span = bytes.AsSpan();

        BinaryPrimitives.WriteUInt16BigEndian(span, 0xFF4F);
        BinaryPrimitives.WriteUInt16BigEndian(span[2..], 0xFF51);
        BinaryPrimitives.WriteUInt16BigEndian(span[4..], 38);
        BinaryPrimitives.WriteUInt32BigEndian(span[8..], xOrigin + width);
        BinaryPrimitives.WriteUInt32BigEndian(span[12..], yOrigin + height);
        BinaryPrimitives.WriteUInt32BigEndian(span[16..], xOrigin);
        BinaryPrimitives.WriteUInt32BigEndian(span[20..], yOrigin);
        BinaryPrimitives.WriteUInt16BigEndian(span[40..], components);

        return bytes;
    }

    private static byte[] Box(string type, byte[] payload)
    {
        var box = new byte[8 + payload.Length];
        BinaryPrimitives.WriteUInt32BigEndian(box, (uint)box.Length);
        Encoding.ASCII.GetBytes(type).CopyTo(box, 4);
        payload.CopyTo(box, 8);
        return box;
    }

    private static byte[] Jp2(uint width, uint height, ushort components)
    {
        var ihdr = new byte[14];
        BinaryPrimitives.WriteUInt32BigEndian(ihdr, height);
        BinaryPrimitives.WriteUInt32BigEndian(ihdr.AsSpan(4), width);
        BinaryPrimitives.WriteUInt16BigEndian(ihdr.AsSpan(8), components);

        byte[] signature = [0x00, 0x00, 0x00, 0x0C, 0x6A, 0x50, 0x20, 0x20, 0x0D, 0x0A, 0x87, 0x0A];

        return [.. signature, .. Box("ftyp", Encoding.ASCII.GetBytes("jp2 \0\0\0\0jp2 ")), .. Box("jp2h", Box("ihdr", ihdr))];
    }

    [Fact]
    public void TheBrokenCodestream_DeclaresItsImpossibleSize()
    {
        Assert.True(J2KHeader.TryRead(BrokenJpc, out var size));
        Assert.Equal(new J2KHeader.Size(203, 2097304, 3), size);
    }

    [Fact]
    public void TheCodestreamOrigin_IsNotPartOfTheSize()
    {
        Assert.True(J2KHeader.TryRead(Codestream(640, 480, 3, xOrigin: 100, yOrigin: 50), out var size));
        Assert.Equal(new J2KHeader.Size(640, 480, 3), size);
    }

    [Fact]
    public void AJp2_DeclaresItsSizeInTheImageHeaderBox()
    {
        Assert.True(J2KHeader.TryRead(Jp2(4000, 3000, 4), out var size));
        Assert.Equal(new J2KHeader.Size(4000, 3000, 4), size);
    }

    [Theory]
    [InlineData(new byte[0])]
    [InlineData(new byte[] { 0xFF, 0x4F, 0xFF, 0x51 })]
    [InlineData(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A })]
    public void AnUnreadableHeader_IsLeftToTheDecoder(byte[] data)
    {
        Assert.False(J2KHeader.TryRead(data, out _));
    }

    [Fact]
    public void AnImpossibleSize_IsRefusedBeforeDecoding()
    {
        var clock = Stopwatch.StartNew();

        var thrown = Assert.Throws<LoadFailureException>(() => J2KHeader.RequireDeclaredSizeFits(BrokenJpc));

        Assert.Equal(LoadFailureKind.TooLarge, thrown.Kind);
        Assert.Equal("Dimensions exceed limit (1048575): 203x2097304", thrown.Message);
        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(1), $"took {clock.Elapsed}");
    }

    [Fact]
    public void AnOrdinarySize_Passes()
    {
        J2KHeader.RequireDeclaredSizeFits(Codestream(4000, 3000, 3));
        J2KHeader.RequireDeclaredSizeFits(Jp2(4000, 3000, 3));
    }
}