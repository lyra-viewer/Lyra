using System.Buffers.Binary;

namespace Lyra.ManagedCodecs.Raster.Png;

public enum ApngDispose : byte
{
    None,
    Background,
    Previous
}

public enum ApngBlend : byte
{
    Source,
    Over
}

/// <summary>A run of bytes in the file: a whole chunk, or a frame's compressed data.</summary>
public readonly record struct ApngSegment(long Offset, int Length);

/// <param name="Data">The frame's zlib stream, in pieces: IDAT payloads, or fdAT payloads less their sequence number.</param>
/// <param name="EncodedBytes">The fcTL and data chunks, headers and CRCs included.</param>
/// <param name="Cut">True when the file ends inside the frame, so its data is short or missing.</param>
public sealed record ApngFrame(
    int X,
    int Y,
    int Width,
    int Height,
    int DelayMs,
    ApngDispose Dispose,
    ApngBlend Blend,
    IReadOnlyList<ApngSegment> Data,
    long EncodedBytes,
    bool Cut
);

/// <param name="DefaultIsFrame">False when the IDAT image is a fallback shown only by decoders without APNG support.</param>
/// <param name="Plays">acTL's play count: 0 plays forever, N plays N times in all.</param>
/// <param name="SharedChunks">Whole chunks before the image data that every frame decodes with: palette, transparency and color.</param>
public sealed record ApngChunks(
    int Width,
    int Height,
    byte BitDepth,
    byte ColorType,
    byte Interlace,
    int DeclaredFrames,
    int Plays,
    bool DefaultIsFrame,
    IReadOnlyList<ApngSegment> SharedChunks,
    bool Truncated,
    IReadOnlyList<ApngFrame> Frames
);

/// <summary>
/// Walks an APNG's chunks for its animation: each frame's rectangle, delay, dispose and blend
/// operations, and where its compressed data lies.
/// </summary>
public static class ApngChunkReader
{
    private const int ChunkOverhead = 12;
    private const int MaxHeaderChunks = 4096;

    public static ReadOnlySpan<byte> Signature => [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    /// <summary>
    /// True when an acTL chunk comes before the image data. Seeks over chunk bodies, so a large
    /// ICC profile or text chunk costs no more than its header.
    /// </summary>
    public static bool DeclaresAnimation(Stream stream)
    {
        Span<byte> header = stackalloc byte[8];

        if (stream.ReadAtLeast(header, 8, throwOnEndOfStream: false) < 8 || !header.SequenceEqual(Signature))
            return false;

        for (var i = 0; i < MaxHeaderChunks; i++)
        {
            if (stream.ReadAtLeast(header, 8, throwOnEndOfStream: false) < 8)
                return false;

            var type = header[4..];
            if (type.SequenceEqual("acTL"u8))
                return true;

            if (type.SequenceEqual("IDAT"u8) || type.SequenceEqual("IEND"u8))
                return false;

            var skip = BinaryPrimitives.ReadUInt32BigEndian(header) + 4L;
            if (skip > stream.Length - stream.Position)
                return false;

            stream.Seek(skip, SeekOrigin.Current);
        }

        return false;
    }

    /// <summary>Null unless the data is a PNG whose acTL precedes its image data.</summary>
    public static ApngChunks? Read(ReadOnlySpan<byte> data)
    {
        if (data.Length < 8 + ChunkOverhead + 13 || !data[..8].SequenceEqual(Signature))
            return null;

        if (BinaryPrimitives.ReadUInt32BigEndian(data[8..]) != 13 || !data.Slice(12, 4).SequenceEqual("IHDR"u8))
            return null;

        var ihdr = data.Slice(16, 13);
        var width = BinaryPrimitives.ReadInt32BigEndian(ihdr);
        var height = BinaryPrimitives.ReadInt32BigEndian(ihdr[4..]);

        // Past 2^31 - 1 reads as negative, and the specification does not allow it either.
        if (width <= 0 || height <= 0)
            return null;

        var shared = new List<ApngSegment>();
        var frames = new List<ApngFrame>();
        int? declared = null;
        var plays = 0;
        var seenImageData = false;
        var defaultIsFrame = false;
        var truncated = true;
        var cutInData = false;

        FrameBuilder? frame = null;

        var pos = 8 + ChunkOverhead + 13;
        while (pos + 8 <= data.Length)
        {
            var length = BinaryPrimitives.ReadUInt32BigEndian(data[pos..]);
            var type = data.Slice(pos + 4, 4);
            var body = pos + 8;

            var isData = type.SequenceEqual("IDAT"u8) || type.SequenceEqual("fdAT"u8);
            var cut = length + 4L > data.Length - body;

            if (cut && !isData)
                break;

            var chunk = data.Slice(body, (int)Math.Min(length, data.Length - body));
            var chunkBytes = cut ? data.Length - pos : ChunkOverhead + chunk.Length;

            if (type.SequenceEqual("IEND"u8))
            {
                truncated = false;
                break;
            }

            if (type.SequenceEqual("acTL"u8))
            {
                if (!seenImageData && chunk.Length >= 8)
                {
                    // The specification caps both at 2^31 - 1; a file past that is not a valid APNG.
                    var frameCount = BinaryPrimitives.ReadUInt32BigEndian(chunk);
                    var playCount = BinaryPrimitives.ReadUInt32BigEndian(chunk[4..]);

                    if (frameCount is 0 or > int.MaxValue || playCount > int.MaxValue)
                        return null;

                    declared = (int)frameCount;
                    plays = (int)playCount;
                }
            }
            else if (type.SequenceEqual("fcTL"u8))
            {
                if (frame is not null)
                    frames.Add(frame.Build(cut: false));

                frame = chunk.Length >= 26 ? new FrameBuilder(chunk, chunkBytes, usesDefaultImage: !seenImageData) : null;
            }
            else if (type.SequenceEqual("IDAT"u8))
            {
                if (!seenImageData)
                    defaultIsFrame = frame is not null;

                seenImageData = true;

                if (frame is { UsesDefaultImage: true })
                    frame.Add(new ApngSegment(body, chunk.Length), chunkBytes);
            }
            else if (type.SequenceEqual("fdAT"u8))
            {
                if (frame is { UsesDefaultImage: false } && chunk.Length > 4)
                    frame.Add(new ApngSegment(body + 4, chunk.Length - 4), chunkBytes);
            }
            else if (!seenImageData && IsDecodingChunk(type))
            {
                shared.Add(new ApngSegment(pos, chunkBytes));
            }

            if (cut)
            {
                cutInData = true;
                break;
            }

            pos += chunkBytes;
        }

        if (frame is not null)
            frames.Add(frame.Build(cut: truncated && (cutInData || frame.IsEmpty)));

        if (declared is not { } declaredFrames)
            return null;

        return new ApngChunks(
            width, height,
            BitDepth: ihdr[8],
            ColorType: ihdr[9],
            Interlace: ihdr[12],
            declaredFrames, plays, defaultIsFrame, shared, truncated, frames
        );
    }

    /// <summary>Chunks that change how pixels decode. Text, EXIF and the rest are left out of each frame.</summary>
    private static bool IsDecodingChunk(ReadOnlySpan<byte> type) =>
        type.SequenceEqual("PLTE"u8) || type.SequenceEqual("tRNS"u8) || type.SequenceEqual("gAMA"u8)
        || type.SequenceEqual("cHRM"u8) || type.SequenceEqual("sRGB"u8) || type.SequenceEqual("iCCP"u8)
        || type.SequenceEqual("sBIT"u8) || type.SequenceEqual("cICP"u8);

    private sealed class FrameBuilder
    {
        private readonly int _x, _y, _width, _height, _delayMs;
        private readonly ApngDispose _dispose;
        private readonly ApngBlend _blend;
        private readonly List<ApngSegment> _data = [];
        private long _encoded;

        /// <summary>True for a frame whose fcTL precedes the IDAT, which then carries its pixels.</summary>
        public bool UsesDefaultImage { get; }

        public bool IsEmpty => _data.Count == 0;

        public FrameBuilder(ReadOnlySpan<byte> fctl, int chunkBytes, bool usesDefaultImage)
        {
            _width = BinaryPrimitives.ReadInt32BigEndian(fctl[4..]);
            _height = BinaryPrimitives.ReadInt32BigEndian(fctl[8..]);
            _x = BinaryPrimitives.ReadInt32BigEndian(fctl[12..]);
            _y = BinaryPrimitives.ReadInt32BigEndian(fctl[16..]);

            var numerator = BinaryPrimitives.ReadUInt16BigEndian(fctl[20..]);
            var denominator = BinaryPrimitives.ReadUInt16BigEndian(fctl[22..]);

            // A zero denominator means hundredths of a second.
            _delayMs = (int)Math.Round(numerator * 1000.0 / (denominator == 0 ? 100 : denominator));

            // Values the specification does not define read as its defaults.
            _dispose = fctl[24] <= 2 ? (ApngDispose)fctl[24] : ApngDispose.None;
            _blend = fctl[25] <= 1 ? (ApngBlend)fctl[25] : ApngBlend.Source;
            _encoded = chunkBytes;
            UsesDefaultImage = usesDefaultImage;
        }

        public void Add(ApngSegment segment, int chunkBytes)
        {
            _data.Add(segment);
            _encoded += chunkBytes;
        }

        public ApngFrame Build(bool cut) => new(_x, _y, _width, _height, _delayMs, _dispose, _blend, _data, _encoded, cut);
    }
}