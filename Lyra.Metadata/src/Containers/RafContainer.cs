using System.Buffers.Binary;
using System.Text;
using Lyra.Metadata.Support;

namespace Lyra.Metadata.Containers;

public readonly record struct RafBlock(long Offset, long Length)
{
    public bool FitsIn(long fileLength) => Offset > 0 && Length <= fileLength - Offset;
}

public sealed record RafHeader(string FormatVersion, string Camera, string DirectoryVersion, RafBlock Jpeg, RafBlock Metadata, RafBlock Cfa);

public sealed class RafContainer : IBufferedContainer
{
    private static ReadOnlySpan<byte> Signature => "FUJIFILMCCD-RAW"u8;

    /// <summary>The header runs to the sensor data's length at 104.</summary>
    public const int HeaderLength = 108;

    private const int ScanLimit = 512;

    public static bool Recognizes(ReadOnlySpan<byte> head) => head.StartsWith(Signature);

    public static RafHeader? ReadHeader(ReadOnlySpan<byte> head)
    {
        if (!Recognizes(head) || head.Length < HeaderLength)
            return null;

        return new RafHeader(
            Encoding.ASCII.GetString(head.Slice(16, 4)),
            Encoding.ASCII.GetString(ByteText.UntilNul(head.Slice(28, 32))).Trim(),
            Encoding.ASCII.GetString(head.Slice(60, 4)),
            Block(head, 84),
            Block(head, 92),
            Block(head, 100)
        );
    }

    private static RafBlock Block(ReadOnlySpan<byte> head, int at) => new(BinaryPrimitives.ReadUInt32BigEndian(head[at..]), BinaryPrimitives.ReadUInt32BigEndian(head[(at + 4)..]));

    public static ContainerContents? Read(ReadOnlySpan<byte> data)
    {
        if (ReadHeader(data) is not { } header)
            return null;

        var jpeg = header.Jpeg;
        if (jpeg.Length > 0 && jpeg.FitsIn(data.Length) && JpegContainer.Recognizes(data[(int)jpeg.Offset..]))
            return JpegContainer.Read(data.Slice((int)jpeg.Offset, (int)jpeg.Length));

        var head = data[..Math.Min(ScanLimit, data.Length)];
        for (var i = 0; i + 3 <= head.Length; i++)
            if (JpegContainer.Recognizes(data[i..]))
                return JpegContainer.Read(data[i..]);

        return null;
    }
}