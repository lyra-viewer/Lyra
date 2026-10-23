using Lyra.Metadata.Layout;

namespace Lyra.Metadata.Containers;

/// <summary>
/// Finds the metadata in a GIF's application extensions, in file order: XMP ("XMP DataXMP") and
/// ICC ("ICCRGBG1012"). The blocks are walked to the trailer; a file cut short keeps what came before.
/// </summary>
public sealed class GifContainer : IBufferedContainer
{
    private const byte ApplicationLabel = 0xFF;

    /// <summary>The bytes XMP appends so its raw packet reads as a chain of sub-blocks.</summary>
    private const int XmpMagicTrailer = 257;

    private const int MaxBlocks = 1_000_000;

    public static bool Recognizes(ReadOnlySpan<byte> head) => GifStream.IsGif(head);

    public static ContainerContents? Read(ReadOnlySpan<byte> data)
    {
        if (!Recognizes(data) || data.Length < 13)
            return null;

        var result = new ContainerContents();
        var ended = false;
        var blocks = 0;

        foreach (var block in GifStream.Children(data))
        {
            if (block.Kind == GifBlockKind.Trailer || ++blocks > MaxBlocks)
            {
                ended = true;
                break;
            }

            // An application extension's first sub-block is its 11-byte identifier.
            if (block.Kind != GifBlockKind.Extension || block.Label != ApplicationLabel || block.SubBlocks is not [11, ..] || block.SubBlocks.Length < 12)
                continue;

            var identifier = block.SubBlocks.Slice(1, 11);
            var rest = block.SubBlocks[12..];

            if (identifier.SequenceEqual("XMP DataXMP"u8))
            {
                // XMP's way: the packet is stored raw, its own bytes read as sub-block lengths, so
                // everything to the terminator is kept - length bytes included - and the magic trailer cut off.
                var raw = rest[..^1];
                if (raw.Length > XmpMagicTrailer)
                    result.Add(MetadataBlockKind.Xmp, raw[..^XmpMagicTrailer].ToArray());
            }
            else if (identifier.SequenceEqual("ICCRGBG1012"u8))
            {
                result.Add(MetadataBlockKind.Icc, GifStream.Join(rest));
            }
        }

        result.Damaged = !ended;
        return result;
    }
}