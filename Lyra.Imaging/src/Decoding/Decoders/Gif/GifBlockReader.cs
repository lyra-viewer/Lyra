using System.Buffers.Binary;
using System.Text;

namespace Lyra.Imaging.Decoding.Decoders.Gif;

/// <param name="DataOffset">Where the LZW minimum code size byte sits, which starts the pixel data.</param>
/// <param name="DataLength">The pixel data's length, its sub-blocks and terminator included.</param>
internal sealed record GifFrameBlock(
    int Width,
    int Height,
    bool Interlaced,
    int LocalPaletteSize,
    bool Transparent,
    long EncodedBytes,
    int DataOffset,
    int DataLength
);

internal sealed record GifBlocks(
    string Version,
    int GlobalPaletteSize,
    int? LoopCount,
    int CommentCount,
    bool Truncated,
    IReadOnlyList<GifFrameBlock> Frames
);

/// <summary>
/// Walks a GIF's block stream for what Skia does not report: each frame's encoded size, palettes,
/// interlacing, transparency and the loop count. Never decodes LZW data, and stops at the first
/// block it cannot make sense of, returning what it read up to there.
/// </summary>
internal static class GifBlockReader
{
    private const byte ExtensionIntroducer = 0x21;
    private const byte ImageSeparator = 0x2C;
    private const byte Trailer = 0x3B;

    private const byte PlainTextLabel = 0x01;
    private const byte GraphicControlLabel = 0xF9;
    private const byte CommentLabel = 0xFE;
    private const byte ApplicationLabel = 0xFF;

    public static GifBlocks? Read(ReadOnlySpan<byte> data)
    {
        if (data.Length < 13 || !(data[..6].SequenceEqual("GIF87a"u8) || data[..6].SequenceEqual("GIF89a"u8)))
            return null;

        var version = Encoding.ASCII.GetString(data[..6]);

        // Logical screen descriptor: size, packed flags, background index, aspect ratio.
        var packed = data[10];
        var globalPalette = (packed & 0x80) != 0 ? 1 << ((packed & 0x07) + 1) : 0;

        var frames = new List<GifFrameBlock>();
        int? loopCount = null;
        var comments = 0;
        var truncated = true;
        var transparent = false;

        var pos = 13 + globalPalette * 3;

        while (pos < data.Length)
        {
            var introducer = data[pos++];
            if (introducer == Trailer)
            {
                truncated = false;
                break;
            }

            if (introducer == ExtensionIntroducer)
            {
                if (pos >= data.Length)
                    break;

                var label = data[pos++];
                var start = pos;

                if (!TrySkipSubBlocks(data, ref pos))
                    break;

                var first = FirstSubBlock(data, start);

                switch (label)
                {
                    case GraphicControlLabel when first.Length >= 4:
                        transparent = (first[0] & 0x01) != 0;
                        break;

                    // A control block governs the next graphic, which may be text rather than a frame.
                    case PlainTextLabel:
                        transparent = false;
                        break;

                    case ApplicationLabel when first.Length == 11 && (first.SequenceEqual("NETSCAPE2.0"u8) || first.SequenceEqual("ANIMEXTS1.0"u8)):
                        var loop = FirstSubBlock(data, start + 1 + first.Length);
                        if (loop.Length >= 3 && loop[0] == 1)
                            loopCount = BinaryPrimitives.ReadUInt16LittleEndian(loop[1..]);
                        break;

                    case CommentLabel:
                        comments++;
                        break;
                }

                continue;
            }

            if (introducer != ImageSeparator)
                break;

            var descriptorStart = pos - 1;
            if (pos + 9 > data.Length)
                break;

            // Left and top precede the size; Skia reports where the frame sits.
            var width = BinaryPrimitives.ReadUInt16LittleEndian(data[(pos + 4)..]);
            var height = BinaryPrimitives.ReadUInt16LittleEndian(data[(pos + 6)..]);
            var flags = data[pos + 8];
            pos += 9;

            var localPalette = (flags & 0x80) != 0 ? 1 << ((flags & 0x07) + 1) : 0;
            pos += localPalette * 3;

            // LZW minimum code size, then the image data.
            var dataOffset = pos;
            pos++;
            if (pos > data.Length || !TrySkipSubBlocks(data, ref pos))
                break;

            frames.Add(new GifFrameBlock(
                width, height,
                Interlaced: (flags & 0x40) != 0,
                LocalPaletteSize: localPalette,
                Transparent: transparent,
                EncodedBytes: pos - descriptorStart,
                DataOffset: dataOffset,
                DataLength: pos - dataOffset)
            );

            transparent = false;
        }

        return new GifBlocks(version, globalPalette, loopCount, comments, truncated, frames);
    }

    /// <summary>Advances past a chain of sub-blocks and its terminator. False when the data ends first.</summary>
    private static bool TrySkipSubBlocks(ReadOnlySpan<byte> data, ref int pos)
    {
        while (pos < data.Length)
        {
            var size = data[pos++];
            if (size == 0)
                return true;

            pos += size;
        }

        return false;
    }

    private static ReadOnlySpan<byte> FirstSubBlock(ReadOnlySpan<byte> data, int pos)
    {
        if (pos >= data.Length)
            return [];

        var size = data[pos];
        return pos + 1 + size <= data.Length ? data.Slice(pos + 1, size) : [];
    }
}