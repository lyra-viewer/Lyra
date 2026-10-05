using Lyra.Imaging.Content;
using Lyra.ManagedCodecs.Raster.Webp;
using SkiaSharp;

namespace Lyra.Imaging.Decoding.Structure;

internal static class WebpFrameSet
{
    public static List<ImageVariant> Describe(SKCodecFrameInfo[] frames, WebpChunks? chunks, int width, int height)
    {
        var encoded = chunks?.Frames.Count == frames.Length ? chunks.Frames : null;

        return
        [
            .. frames.Select((frame, i) => new ImageVariant(
                Label: $"Frame {i + 1}",
                Width: width,
                Height: height,
                Detail: FrameFacts.DescribeFrame(frame),
                ByteSize: encoded?[i].EncodedBytes))
        ];
    }
    
    public static void Describe(Composite composite, SKCodecFrameInfo[] frames, WebpChunks? chunks)
    {
        var animated = frames.Length > 1;

        if (chunks is not null && DescribeCompression(chunks.Frames) is { } compression)
            composite.AddFormatSpecific("Compression", compression);

        if (chunks is { Frames.Count: 0 })
            composite.AddFormatSpecific("Frames", "none");

        if (animated)
        {
            composite.AddFormatSpecific("Frames", frames.Length.ToString());
            composite.AddFormatSpecific("Duration", FrameFacts.TotalDuration(frames));

            if (chunks?.LoopCount is { } loop)
                composite.AddFormatSpecific("Loop", FrameFacts.DescribePlays(loop));
        }

        if (chunks is { Truncated: true })
        {
            var where = chunks.CutFrame ? $"ends inside frame {chunks.Frames.Count + 1}, which is not shown" : "stops inside a chunk";
            composite.AddFormatSpecific("Truncated", where);
        }
    }

    public static string WhyNoImage(WebpChunks? chunks)
    {
        if (chunks is null)
            return "The file has no valid WebP header";

        var empty = FirstEmptyFrame(chunks.Frames);

        return chunks switch
        {
            _ when empty >= 0 => $"Frame {empty + 1} has no image data, which makes the whole animation unreadable",
            { Frames.Count: 0, Truncated: true } => "The WebP ends before its first frame",
            { Frames.Count: 0 } => "The WebP holds no frames",
            { LoopCount: null } => "The animation has no ANIM chunk, which it requires",
            _ => "No frame in the WebP could be read"
        };
    }

    private static int FirstEmptyFrame(IReadOnlyList<WebpFrameChunk> frames)
    {
        for (var i = 0; i < frames.Count; i++)
        {
            if (frames[i].Encoding == WebpFrameEncoding.None)
                return i;
        }

        return -1;
    }

    private static string? DescribeCompression(IReadOnlyList<WebpFrameChunk> frames)
    {
        var coded = frames.Count(f => f.Encoding != WebpFrameEncoding.None);
        if (coded == 0)
            return null;

        var lossless = frames.Count(f => f.Encoding == WebpFrameEncoding.Lossless);

        return lossless == 0 ? "lossy"
            : lossless == coded ? "lossless"
            : $"{lossless} of {coded} frames lossless";
    }
}