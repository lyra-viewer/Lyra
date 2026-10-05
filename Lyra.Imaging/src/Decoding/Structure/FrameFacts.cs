using Lyra.Common;
using Lyra.Imaging.Content;
using SkiaSharp;

namespace Lyra.Imaging.Decoding.Structure;

/// <summary>Wording shared by the animated formats.</summary>
internal static class FrameFacts
{
    public static string DescribeFrame(SKCodecFrameInfo frame) => DescribeFrame(frame.FrameRect, frame.Duration);

    public static string DescribeFrame(SKRectI rect, int durationMs)
    {
        var offset = rect.Left != 0 || rect.Top != 0 ? $" at {rect.Left},{rect.Top}" : "";
        return $"{rect.Width}x{rect.Height}{offset}, {Formatters.DurationToStr(Math.Max(0, durationMs))}";
    }

    public static List<ImageVariant> WholeFrames(IReadOnlyList<int> durationsMs, int width, int height, Func<int, long?>? encodedBytes = null) =>
    [
        .. durationsMs.Select((ms, i) => new ImageVariant(
            Label: $"Frame {i + 1}",
            Width: width,
            Height: height,
            Detail: DescribeFrame(SKRectI.Create(width, height), ms),
            ByteSize: encodedBytes?.Invoke(i))
        )
    ];

    public static string TotalDuration(SKCodecFrameInfo[] frames) => TotalDuration(frames.Select(f => f.Duration));

    public static string TotalDuration(IEnumerable<int> durationsMs) => Formatters.DurationToStr(durationsMs.Sum(d => (long)Math.Max(0, d)));

    public static string Proportion(int count, int total) => total == 1 ? "yes" : count == total ? "all frames" : $"{count} of {total} frames";

    public static string DescribePlays(int plays) => plays switch
    {
        0 => "forever",
        1 => "plays once",
        2 => "1 repeat",
        _ => $"{plays - 1} repeats"
    };
}