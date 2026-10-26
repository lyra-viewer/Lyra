using System.Buffers.Binary;
using System.Text;
using Lyra.Metadata.Layout;

namespace Lyra.ManagedCodecs.Raster.Gif;

/// <param name="DataOffset">Where the LZW minimum code size byte sits, which starts the pixel data.</param>
/// <param name="DataLength">The pixel data's length, its sub-blocks and terminator included.</param>
public sealed record GifFrameBlock(
    int Width,
    int Height,
    bool Interlaced,
    int LocalPaletteSize,
    bool Transparent,
    long EncodedBytes,
    int DataOffset,
    int DataLength
);

public sealed record GifBlocks(
    string Version,
    int GlobalPaletteSize,
    int? LoopCount,
    int CommentCount,
    bool Truncated,
    IReadOnlyList<GifFrameBlock> Frames
);

/// <summary>
/// Walks a GIF's block stream for its structure: each frame's size, encoded size, palette,
/// interlacing and transparency, and the loop count. Never decodes LZW data; the pixels are left
/// to whatever decodes them. Stops at the first block it cannot make sense of, returning what it
/// read up to there.
/// </summary>
public static class GifBlockReader
{

    private const byte PlainTextLabel = 0x01;
    private const byte GraphicControlLabel = 0xF9;
    private const byte CommentLabel = 0xFE;
    private const byte ApplicationLabel = 0xFF;

    public static GifBlocks? Read(ReadOnlySpan<byte> data)
    {
        if (data.Length < 13 || !GifStream.IsGif(data))
            return null;

        var version = Encoding.ASCII.GetString(data[..6]);

        var globalPalette = GifStream.GlobalPaletteSize(data);

        var frames = new List<GifFrameBlock>();
        int? loopCount = null;
        var comments = 0;
        var truncated = true;
        var transparent = false;

        foreach (var block in GifStream.Children(data))
        {
            switch (block.Kind)
            {
                case GifBlockKind.Trailer:
                    truncated = false;
                    break;

                case GifBlockKind.Extension:
                    var first = GifStream.FirstSubBlock(block.SubBlocks);
                    switch (block.Label)
                    {
                        case GraphicControlLabel when first.Length >= 4:
                            transparent = (first[0] & 0x01) != 0;
                            break;

                        // A control block governs the next graphic, which may be text rather than a frame.
                        case PlainTextLabel:
                            transparent = false;
                            break;

                        case ApplicationLabel when first.Length == 11 && (first.SequenceEqual("NETSCAPE2.0"u8) || first.SequenceEqual("ANIMEXTS1.0"u8)):
                            var loop = GifStream.FirstSubBlock(block.SubBlocks[(1 + first.Length)..]);
                            if (loop.Length >= 3 && loop[0] == 1)
                                loopCount = BinaryPrimitives.ReadUInt16LittleEndian(loop[1..]);
                            break;

                        case CommentLabel:
                            comments++;
                            break;
                    }
                    break;

                case GifBlockKind.Image:
                    // Left and top precede the size; the pixel decoder reports where the frame sits.
                    var flags = block.Descriptor[8];
                    frames.Add(new GifFrameBlock(
                        Width: BinaryPrimitives.ReadUInt16LittleEndian(block.Descriptor[4..]),
                        Height: BinaryPrimitives.ReadUInt16LittleEndian(block.Descriptor[6..]),
                        Interlaced: (flags & 0x40) != 0,
                        LocalPaletteSize: (flags & 0x80) != 0 ? 1 << ((flags & 0x07) + 1) : 0,
                        Transparent: transparent,
                        EncodedBytes: block.End - block.Offset,
                        DataOffset: block.DataOffset,
                        DataLength: block.End - block.DataOffset)
                    );

                    transparent = false;
                    break;
            }
        }

        return new GifBlocks(version, globalPalette, loopCount, comments, truncated, frames);
    }
}
