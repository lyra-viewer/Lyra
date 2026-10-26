using System.Buffers.Binary;
using Lyra.Metadata.Layout;

namespace Lyra.ManagedCodecs.Raster.Webp;

public enum WebpFrameEncoding
{
    None,
    Lossy,
    Lossless
}

/// <param name="Encoding">None when the frame carries no VP8 or VP8L bitstream.</param>
/// <param name="EncodedBytes">The whole ANMF chunk, header and padding included.</param>
public sealed record WebpFrameChunk(WebpFrameEncoding Encoding, long EncodedBytes);

/// <param name="LoopCount">ANIM's loop count: 0 plays forever, N plays N times in all; null without ANIM.</param>
/// <param name="CutFrame">True when the data ends inside a frame, which is then not listed.</param>
public sealed record WebpChunks(int? LoopCount, bool Truncated, bool CutFrame, IReadOnlyList<WebpFrameChunk> Frames);

/// <summary>
/// Walks an animated WebP's RIFF chunks for its structure: each frame's encoding and encoded
/// size, and the loop count. Never decodes a bitstream; the frames' geometry is left to whatever
/// decodes them. Stops at the first chunk that runs past the data, returning what it read up to there.
/// </summary>
public static class WebpChunkReader
{
    /// <summary>The RIFF header and a VP8X chunk: enough to tell whether a file is animated.</summary>
    public const int HeaderLength = 30;

    private const int ChunkHeader = 8;
    private const int Vp8xAnimationFlag = 0x02;

    /// <summary>True when the file opens with a VP8X chunk whose animation flag is set.</summary>
    public static bool DeclaresAnimation(ReadOnlySpan<byte> header) => IsExtended(header) && (header[20] & Vp8xAnimationFlag) != 0;

    /// <summary>Null unless the data is a WebP in the extended format, the only one that animates.</summary>
    public static WebpChunks? Read(ReadOnlySpan<byte> data)
    {
        if (!IsExtended(data))
            return null;

        var riffEnd = 8L + BinaryPrimitives.ReadUInt32LittleEndian(data[4..]);
        var form = RiffChunks.Form(data);
        var end = form.Length;

        var frames = new List<WebpFrameChunk>();
        int? loopCount = null;
        var truncated = riffEnd > data.Length;
        var cutFrame = false;

        var pos = 12L;
        foreach (var chunk in RiffChunks.Children(form, start: 12, keepCutShort: true))
        {
            if (chunk.CutShort)
            {
                truncated = true;
                cutFrame = chunk.Is("ANMF"u8);
                break;
            }

            if (chunk.Is("ANIM"u8) && chunk.Body.Length >= 6)
                loopCount = BinaryPrimitives.ReadUInt16LittleEndian(chunk.Body[4..]);
            else if (chunk.Is("ANMF"u8) && chunk.Body.Length >= 16)
                frames.Add(ReadFrame(chunk.Body, Math.Min(chunk.Next, end) - chunk.Offset));

            pos = chunk.Next;
        }

        if (pos < end && pos + ChunkHeader > end)
            truncated = true;

        return new WebpChunks(loopCount, truncated, cutFrame, frames);
    }

    /// <summary>The bitstream after ANMF's 16-byte frame header: an optional ALPH chunk, then VP8 or VP8L.</summary>
    private static WebpFrameChunk ReadFrame(ReadOnlySpan<byte> body, long encodedBytes)
    {
        foreach (var chunk in RiffChunks.Children(body, start: 16))
        {
            if (chunk.Is("VP8 "u8))
                return new WebpFrameChunk(WebpFrameEncoding.Lossy, encodedBytes);

            if (chunk.Is("VP8L"u8))
                return new WebpFrameChunk(WebpFrameEncoding.Lossless, encodedBytes);
        }

        return new WebpFrameChunk(WebpFrameEncoding.None, encodedBytes);
    }

    private static bool IsExtended(ReadOnlySpan<byte> data) =>
        data.Length >= HeaderLength
        && data[..4].SequenceEqual("RIFF"u8)
        && data.Slice(8, 4).SequenceEqual("WEBP"u8)
        && data.Slice(12, 4).SequenceEqual("VP8X"u8);
}