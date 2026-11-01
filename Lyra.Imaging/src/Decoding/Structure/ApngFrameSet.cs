using Lyra.Imaging.Content;
using Lyra.Imaging.Content.Variants;
using Lyra.ManagedCodecs.Raster.Png;
using SkiaSharp;

namespace Lyra.Imaging.Decoding.Structure;

internal static class ApngFrameSet
{
    public static List<ImageVariant> Describe(ApngChunks chunks) =>
    [
        .. chunks.Frames.Select((frame, i) => new ImageVariant(
            Label: $"Frame {i + 1}",
            Width: chunks.Width,
            Height: chunks.Height,
            Detail: FrameFacts.DescribeFrame(SKRectI.Create(frame.X, frame.Y, frame.Width, frame.Height), frame.DelayMs),
            ByteSize: frame.EncodedBytes)
        )
    ];

    /// <summary>The animation's facts, for the format panel; the PNG's own are in its metadata.</summary>
    public static void Describe(DecodeContext context, ApngChunks chunks)
    {
        var frames = chunks.Frames;

        if (frames.Count == 0)
            context.AddFormatSpecific("Frames", "none");

        if (frames.Count > 1)
        {
            context.AddFormatSpecific("Frames", frames.Count.ToString());
            context.AddFormatSpecific("Duration", FrameFacts.TotalDuration(frames.Select(f => f.DelayMs)));
            context.AddFormatSpecific("Loop", FrameFacts.DescribePlays(chunks.Plays));
        }

        if (!chunks.DefaultIsFrame && frames.Count > 0)
            context.AddFormatSpecific("Default Image", "a fallback, not part of the animation");

        if (chunks.DeclaredFrames != frames.Count && !chunks.Truncated)
            context.AddFormatSpecific("Declared Frames", chunks.DeclaredFrames.ToString());

        if (chunks.Truncated)
            context.AddFormatSpecific("Truncated", "stops before the end chunk");
    }
}