using System.Buffers.Binary;
using System.Text;
using Lyra.Metadata.Support;

namespace Lyra.Metadata.Containers;

/// <summary>One block of an MRW header: its tag ("PRD", "TTW"...), where it starts and how long its data is.</summary>
public readonly record struct MrwBlock(string Tag, long Offset, long DataLength)
{
    /// <summary>Where the block's data starts, past its 8-byte header.</summary>
    public long DataOffset => Offset + 8;
}

public sealed record MrwLayout(long DataOffset, IReadOnlyList<MrwBlock> Blocks);

/// <summary>
/// A Minolta MRW: a header of tagged blocks - one of them, TTW, a whole TIFF with the EXIF - and
/// the raw data after it.
/// </summary>
public sealed class MrwContainer : IStreamedContainer
{
    private const int MaxBlocks = 64;

    public static bool Recognizes(ReadOnlySpan<byte> head) => head.StartsWith("\0MRM"u8);

    public static ContainerContents? Read(Stream stream)
    {
        if (ReadLayout(stream) is not { } layout)
            return null;

        var container = new ContainerContents();
        container.AddIfPresent(MetadataBlockKind.Exif, Tiff(stream, layout));
        return container;
    }

    public static MrwLayout? ReadLayout(Stream stream)
    {
        if (!stream.CanSeek)
            return null;

        using var _ = stream.KeepPosition();

        Span<byte> header = stackalloc byte[8];
        if (!stream.ReadAt(0, header) || !Recognizes(header))
            return null;

        var dataOffset = 8L + BinaryPrimitives.ReadUInt32BigEndian(header[4..]);
        var end = Math.Min(dataOffset, stream.Length);
        var blocks = new List<MrwBlock>();

        var offset = 8L;
        while (offset + 8 <= end && blocks.Count < MaxBlocks)
        {
            if (!stream.ReadAt(offset, header))
                break;

            var length = BinaryPrimitives.ReadUInt32BigEndian(header[4..]);
            blocks.Add(new MrwBlock(Encoding.ASCII.GetString(header[1..4]), offset, length));
            offset += 8L + length;
        }

        return new MrwLayout(dataOffset, blocks);
    }

    private static byte[]? Tiff(Stream stream, MrwLayout layout)
    {
        if (layout.Blocks.FirstOrDefault(block => block.Tag == "TTW") is not { DataLength: > 0 } ttw)
            return null;

        using var _ = stream.KeepPosition();
        return stream.ReadBytesAt(ttw.DataOffset, ttw.DataLength, Limits.BlockBytes);
    }
}