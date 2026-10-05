using Lyra.Common;
using Lyra.Imaging.Content;
using SkiaSharp;

namespace Lyra.Imaging.Decoding.Decoders.Animation;

/// <summary>
/// Renders any frame of an animation as it looks when shown: composited over the frames it
/// depends on, not just its own rectangle. Keeps the last frame it rendered, so the frame after
/// it costs one decode rather than a replay from the nearest independent frame.
/// </summary>
internal sealed class AnimationFrameRenderer(SKImageInfo info, Func<int, string?>? explain = null, Action<int, string>? damaged = null)
    : ExclusiveResource
{
    private SKBitmap? _canvas;
    private int _canvasFrame = -1;

    private readonly Dictionary<int, string> _damage = new();

    public SKImageInfo Info => info;

    public SKBitmap Render(SKCodec codec, int index, CancellationToken ct) => Exclusive(() => RenderLocked(codec, index, ct));

    private SKBitmap RenderLocked(SKCodec codec, int index, CancellationToken ct)
    {
        var frames = codec.FrameInfo;
        if (index < 0 || index >= Math.Max(1, frames.Length))
            throw new ArgumentOutOfRangeException(nameof(index));

        if (index == _canvasFrame && _canvas is not null)
            return Snapshot(_canvas);

        var chain = new List<int> { index };
        var prior = -1;

        // Skia guarantees a frame depends only on an earlier one; the bound keeps a bad table finite.
        for (var f = RequiredFrame(frames, index); f >= 0; f = RequiredFrame(frames, f))
        {
            if (f >= chain[^1])
                throw new InvalidDataException($"Frame {chain[^1]} depends on frame {f}, which does not precede it.");

            if (f == _canvasFrame)
            {
                prior = f;
                break;
            }

            chain.Add(f);
        }

        if (prior < 0 || _canvas is null)
        {
            _canvas ??= Allocate();
            _canvas.Erase(SKColors.Transparent);
            prior = -1;
        }

        for (var i = chain.Count - 1; i >= 0; i--)
        {
            ct.ThrowIfCancellationRequested();

            var frame = chain[i];

            _canvasFrame = -1;
            var result = codec.GetPixels(info, _canvas.GetPixels(), new SKCodecOptions(frame, prior));

            if (result != SKCodecResult.Success)
                AcceptDamaged(frame, result);

            _canvasFrame = prior = frame;
        }

        return Snapshot(_canvas);
    }

    private SKBitmap Snapshot(SKBitmap canvas) =>
        canvas.Copy() ?? throw new OutOfMemoryException($"Could not copy a {info.Width}x{info.Height} frame.");

    /// <summary>
    /// Keeps what Skia decoded of a frame it did not finish, which it leaves in the canvas with the
    /// rest cleared: a frame the file ends inside, or one whose rejection can be explained. Throws
    /// for a rejection that cannot.
    /// </summary>
    private void AcceptDamaged(int frame, SKCodecResult result)
    {
        if (_damage.ContainsKey(frame))
            return;

        // Not cached when unexplained: the reason may be readable on a later attempt.
        var why = result == SKCodecResult.IncompleteInput
            ? $"Frame {frame + 1} is cut short: the file ends inside it"
            : explain?.Invoke(frame) ?? throw new LoadFailureException(LoadFailureKind.DecodeFailed, $"Frame {frame + 1} could not be decoded ({result})");

        _damage[frame] = why;

        Logger.Warning($"[{nameof(AnimationFrameRenderer)}] {why}; shown as far as it goes, the rest left transparent.");
        damaged?.Invoke(frame, why);
    }

    private static int RequiredFrame(SKCodecFrameInfo[] frames, int index) =>
        index < frames.Length ? frames[index].RequiredFrame : -1;

    private SKBitmap Allocate()
    {
        var bitmap = new SKBitmap(info);
        if (bitmap.GetPixels() == IntPtr.Zero)
        {
            bitmap.Dispose();
            throw new OutOfMemoryException($"Could not allocate a {info.Width}x{info.Height} canvas.");
        }

        return bitmap;
    }

    protected override void Release()
    {
        _canvas?.Dispose();
        _canvas = null;
        _canvasFrame = -1;
    }

}
