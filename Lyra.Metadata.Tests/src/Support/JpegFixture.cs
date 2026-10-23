using System.Buffers.Binary;
using System.Text;

namespace Lyra.Metadata.Tests.Support;

/// <summary>Builds JPEG headers from segments: no image data, which metadata readers never reach.</summary>
internal static class JpegFixture
{
    public static byte[] Build(params byte[][] segments) =>
    [
        0xFF, 0xD8,
        .. segments.SelectMany(segment => segment),
        .. Segment(0xDA, [1, 1, 0, 0, 63, 0]),
        0xFF, 0xD9
    ];

    public static byte[] Segment(byte marker, byte[] payload)
    {
        var length = new byte[2];
        BinaryPrimitives.WriteUInt16BigEndian(length, (ushort)(payload.Length + 2));
        return [0xFF, marker, .. length, .. payload];
    }

    public static byte[] Exif(byte[] tiff) => Segment(0xE1, [.. "Exif\0\0"u8, .. tiff]);

    public static byte[] Xmp(string packet) => Segment(0xE1, [.. "http://ns.adobe.com/xap/1.0/\0"u8, .. Encoding.UTF8.GetBytes(packet)]);

    public static byte[][] Icc(byte[] profile, int chunkSize = 60_000)
    {
        var chunks = profile.Chunk(chunkSize).ToList();
        return chunks.Select((chunk, i) => Segment(0xE2, [.. "ICC_PROFILE\0"u8, (byte)(i + 1), (byte)chunks.Count, .. chunk])).ToArray();
    }

    public static byte[] Photoshop(byte[] resources) => Segment(0xED, [.. "Photoshop 3.0\0"u8, .. resources]);

    public static byte[] Frame(byte marker = 0xC0, byte precision = 8, int components = 3)
    {
        return Segment(marker, [precision, 0, 8, 0, 8, (byte)components, .. Enumerable.Range(1, components).SelectMany(i => new byte[] { (byte)i, 0x11, 0 })]);
    }
}