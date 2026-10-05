using Lyra.Imaging.Content;
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
    public static void Describe(Composite composite, ApngChunks chunks)
    {
        var frames = chunks.Frames;

        if (frames.Count == 0)
            composite.AddFormatSpecific("Frames", "none");

        if (frames.Count > 1)
        {
            composite.AddFormatSpecific("Frames", frames.Count.ToString());
            composite.AddFormatSpecific("Duration", FrameFacts.TotalDuration(frames.Select(f => f.DelayMs)));
            composite.AddFormatSpecific("Loop", FrameFacts.DescribePlays(chunks.Plays));
        }

        if (!chunks.DefaultIsFrame && frames.Count > 0)
            composite.AddFormatSpecific("Default Image", "a fallback, not part of the animation");

        if (chunks.DeclaredFrames != frames.Count && !chunks.Truncated)
            composite.AddFormatSpecific("Declared Frames", chunks.DeclaredFrames.ToString());

        if (chunks.Truncated)
            composite.AddFormatSpecific("Truncated", "stops before the end chunk");
    }
}