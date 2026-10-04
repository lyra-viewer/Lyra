using Lyra.Common;
using Lyra.Imaging.Content;
using Lyra.Imaging.Decoding.Decoders.Gif;
using SkiaSharp;

namespace Lyra.Imaging.Decoding.Structure;

internal static class GifFrameSet
{
    /// <summary>
    /// One entry per frame Skia can decode. Encoded sizes come from the block walk only when it
    /// found the same frames, since a damaged file can make the two disagree.
    /// </summary>
    public static List<ImageVariant> Describe(SKCodecFrameInfo[] frames, GifBlocks? blocks, int width, int height)
    {
        var encoded = blocks?.Frames.Count == frames.Length ? blocks.Frames : null;

        return
        [
            .. frames.Select((frame, i) => new ImageVariant(
                Label: $"Frame {i + 1}",
                Width: width,
                Height: height,
                Detail: DescribeFrame(frame),
                ByteSize: encoded?[i].EncodedBytes))
        ];
    }

    /// <summary>
    /// The file's facts, for the format panel. A GIF has no structure beyond its frames, which the
    /// frame list already shows, so everything worth knowing about the stream goes here.
    /// </summary>
    public static void Describe(Composite composite, SKCodecFrameInfo[] frames, GifBlocks? blocks)
    {
        var animated = frames.Length > 1;

        composite.AddFormatSpecific("Format", blocks?.Version ?? "GIF");

        if (blocks is { Frames.Count: 0 })
            composite.AddFormatSpecific("Frames", "none");

        if (animated)
        {
            composite.AddFormatSpecific("Frames", frames.Length.ToString());
            composite.AddFormatSpecific("Duration", TotalDuration(frames));
        }

        if (blocks is null)
            return;

        if (animated)
            composite.AddFormatSpecific("Loop", DescribeLoop(blocks.LoopCount));

        composite.AddFormatSpecific("Palette", DescribePalette(blocks));

        AddWhenAny(composite, "Interlaced", blocks, f => f.Interlaced);
        AddWhenAny(composite, "Transparency", blocks, f => f.Transparent);

        if (blocks.CommentCount > 0)
            composite.AddFormatSpecific("Comments", blocks.CommentCount.ToString());

        if (blocks.Truncated)
            composite.AddFormatSpecific("Truncated", "stops before the trailer");
    }

    /// <summary>The one reason when a single frame is damaged; otherwise which frames, by number.</summary>
    public static string DescribeDamage(IReadOnlyDictionary<int, string> damaged)
    {
        if (damaged.Count == 1)
            return damaged.Values.Single();

        var shown = string.Join(", ", damaged.Keys.Order().Take(5).Select(index => index + 1));
        return $"{damaged.Count} frames: {shown}{(damaged.Count > 5 ? ", ..." : "")}";
    }

    /// <summary>"yes" for a still image; for an animation, how many of its frames.</summary>
    private static void AddWhenAny(Composite composite, string key, GifBlocks blocks, Func<GifFrameBlock, bool> has)
    {
        var count = blocks.Frames.Count(has);
        if (count == 0)
            return;

        var total = blocks.Frames.Count;
        composite.AddFormatSpecific(key, total == 1 ? "yes" : count == total ? "all frames" : $"{count} of {total} frames");
    }

    /// <summary>The frame's own rectangle, its offset when it has one, and its delay.</summary>
    private static string DescribeFrame(SKCodecFrameInfo frame)
    {
        var rect = frame.FrameRect;
        var offset = rect.Left != 0 || rect.Top != 0 ? $" at {rect.Left},{rect.Top}" : "";

        return $"{rect.Width}x{rect.Height}{offset}, {Formatters.DurationToStr(Math.Max(0, frame.Duration))}";
    }

    private static string DescribePalette(GifBlocks blocks)
    {
        var locals = blocks.Frames.Count(f => f.LocalPaletteSize > 0);

        if (blocks.GlobalPaletteSize == 0)
            return locals > 0 ? "local only" : "none";

        var global = $"{blocks.GlobalPaletteSize} colors";
        return locals > 0 ? $"{global}, {locals} local" : global;
    }

    private static string DescribeLoop(int? loopCount) => loopCount switch
    {
        null => "plays once",
        0    => "forever",
        1    => "1 repeat",
        _    => $"{loopCount} repeats"
    };

    private static string TotalDuration(SKCodecFrameInfo[] frames) =>
        Formatters.DurationToStr(frames.Sum(f => (long)Math.Max(0, f.Duration)));
}