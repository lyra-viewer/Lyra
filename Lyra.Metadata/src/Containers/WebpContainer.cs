using System.Buffers.Binary;
using Lyra.Metadata.Exif;
using Lyra.Metadata.Support;

namespace Lyra.Metadata.Containers;

public sealed class WebpContainer : IStreamedContainer
{
    private const int MaxChunks = 100_000;

    public static bool Recognizes(ReadOnlySpan<byte> head) => head.Length >= 12 && head.StartsWith("RIFF"u8) && head[8..12].SequenceEqual("WEBP"u8);

    public static ContainerContents? Read(Stream stream)
    {
        if (!stream.CanSeek)
            return null;

        using var _ = stream.KeepPosition();

        Span<byte> header = stackalloc byte[12];
        if (!stream.ReadAt(0, header) || !Recognizes(header))
            return null;

        var result = new ContainerContents();
        var form = Math.Min(stream.Length, 8L + BinaryPrimitives.ReadUInt32LittleEndian(header[4..]));
        var offset = 12L;

        // Chunk headers only; the image data is seeked over.
        for (var chunks = 0; chunks < MaxChunks && offset + 8 <= form && stream.ReadAt(offset, header[..8]); chunks++)
        {
            var size = BinaryPrimitives.ReadUInt32LittleEndian(header[4..]);
            if (size > form - offset - 8)
            {
                result.Damaged = true;
                break;
            }

            if (BlockKind(header[..4]) is { } kind && stream.ReadBytesAt(offset + 8, size, Limits.BlockBytes) is { } body)
                result.Add(kind, kind == MetadataBlockKind.Exif && ExifPayload.HasPreamble(body) ? body[ExifPayload.Preamble.Length..] : body);

            offset += 8L + size + (size & 1);
        }

        return result;
    }

    private static MetadataBlockKind? BlockKind(ReadOnlySpan<byte> fourCc) =>
        fourCc.SequenceEqual("EXIF"u8) ? MetadataBlockKind.Exif
        : fourCc.SequenceEqual("XMP "u8) ? MetadataBlockKind.Xmp
        : fourCc.SequenceEqual("ICCP"u8) ? MetadataBlockKind.Icc
        : null;
}
