using Lyra.Common;
using Lyra.ManagedCodecs.Raster.Png;
using SkiaSharp;

namespace Lyra.Imaging.Decoding.Decoders.Animation;

internal sealed class ApngFrameRenderer(Func<Stream> open, ApngChunks chunks, SKImageInfo info, Action<int, string>? damaged = null)
    : ExclusiveResource
{
    private SKBitmap? _canvas;
    private int _canvasFrame = -1;

    /// <summary>What lay under <see cref="_canvasFrame"/> before it was drawn, when it disposes to previous.</summary>
    private SKBitmap? _underneath;

    private readonly Dictionary<int, string> _damage = new();

    private IReadOnlyList<ApngFrame> Frames => chunks.Frames;

    public int Count => Frames.Count;

    public SKBitmap Render(int index, CancellationToken ct) => Exclusive(() => RenderLocked(index, ct));

    private SKBitmap RenderLocked(int index, CancellationToken ct)
    {
        if (index < 0 || index >= Frames.Count)
            throw new ArgumentOutOfRangeException(nameof(index));

        if (index == _canvasFrame && _canvas is not null)
            return Snapshot(_canvas);

        var start = IndependentStart(index);
        int from;

        if (_canvas is not null && _canvasFrame >= start && _canvasFrame < index)
            from = _canvasFrame + 1;
        else
        {
            _canvas ??= Allocate();
            _canvas.Erase(SKColors.Transparent);
            DropUnderneath();
            _canvasFrame = -1;
            from = start;
        }

        using var stream = open();

        for (var f = from; f <= index; f++)
        {
            ct.ThrowIfCancellationRequested();

            var prior = _canvasFrame;
            _canvasFrame = -1;

            if (prior >= 0)
                DisposeOf(prior);

            Draw(stream, f);
            _canvasFrame = f;
        }

        return Snapshot(_canvas);
    }
    
    private int IndependentStart(int index)
    {
        for (var f = index; f > 0; f--)
        {
            if (Frames[f].Blend == ApngBlend.Source && CoversCanvas(Frames[f]))
                return f;

            if (EffectiveDispose(f - 1) == ApngDispose.Background && CoversCanvas(Frames[f - 1]))
                return f;
        }

        return 0;
    }

    private void Draw(Stream stream, int index)
    {
        var frame = Frames[index];

        if (!InsideCanvas(frame))
        {
            Damage(index, $"Frame {index + 1} lies outside the {info.Width}x{info.Height} canvas, so is not drawn");
            return;
        }

        var rect = SKRectI.Create(frame.X, frame.Y, frame.Width, frame.Height);

        if (EffectiveDispose(index) == ApngDispose.Previous)
            _underneath = Copy(rect);

        using var pixels = Decode(stream, index);
        if (pixels is null)
        {
            if (frame.Blend == ApngBlend.Source)
                _canvas!.Erase(SKColors.Transparent, rect);

            return;
        }

        using var canvas = new SKCanvas(_canvas);
        using var paint = new SKPaint();
        paint.BlendMode = frame.Blend == ApngBlend.Source ? SKBlendMode.Src : SKBlendMode.SrcOver;
        canvas.DrawBitmap(pixels, frame.X, frame.Y, SKSamplingOptions.Default, paint);
    }

    private void DisposeOf(int index)
    {
        var frame = Frames[index];

        if (!InsideCanvas(frame))
            return;

        var rect = SKRectI.Create(frame.X, frame.Y, frame.Width, frame.Height);

        switch (EffectiveDispose(index))
        {
            case ApngDispose.Background:
                _canvas!.Erase(SKColors.Transparent, rect);
                break;

            case ApngDispose.Previous when _underneath is not null:
                using (var canvas = new SKCanvas(_canvas))
                using (var paint = new SKPaint())
                {
                    paint.BlendMode = SKBlendMode.Src;
                    canvas.DrawBitmap(_underneath, rect.Left, rect.Top, SKSamplingOptions.Default, paint);
                }

                break;
        }

        DropUnderneath();
    }

    private SKBitmap? Decode(Stream stream, int index)
    {
        var frame = Frames[index];

        if (frame.Data.Count == 0)
        {
            Damage(index, frame.Cut ? $"Frame {index + 1} is cut off: the file ends before its image data" : $"Frame {index + 1} has no image data");
            return null;
        }

        using var png = new MemoryStream(ApngFramePng.Build(stream, chunks, frame), writable: false);
        using var codec = SKCodec.Create(png);

        if (codec is null || codec.Info.Width != frame.Width || codec.Info.Height != frame.Height)
        {
            Damage(index, $"Frame {index + 1} could not be decoded");
            return null;
        }

        var bitmap = new SKBitmap(new SKImageInfo(frame.Width, frame.Height, SKColorType.Rgba8888, SKAlphaType.Premul, info.ColorSpace));
        bitmap.Erase(SKColors.Transparent);

        var result = codec.GetPixels(bitmap.Info, bitmap.GetPixels());

        switch (result)
        {
            case SKCodecResult.Success:
                return bitmap;

            // Skia cannot tell a short zlib stream from a corrupt one, but the chunk walk can.
            case SKCodecResult.IncompleteInput or SKCodecResult.ErrorInInput:
                Damage(index, frame.Cut ? $"Frame {index + 1} is cut short: the file ends inside it" : $"Frame {index + 1}'s image data is corrupt");
                return bitmap;

            default:
                bitmap.Dispose();
                Damage(index, $"Frame {index + 1} could not be decoded ({result})");
                return null;
        }
    }

    /// <summary>The APNG rule that a first frame disposing to previous disposes to background.</summary>
    private ApngDispose EffectiveDispose(int index) =>
        index == 0 && Frames[0].Dispose == ApngDispose.Previous ? ApngDispose.Background : Frames[index].Dispose;

    private bool CoversCanvas(ApngFrame frame) => frame.X == 0 && frame.Y == 0 && frame.Width == info.Width && frame.Height == info.Height;

    private bool InsideCanvas(ApngFrame frame) =>
        frame is { X: >= 0, Y: >= 0, Width: > 0, Height: > 0 }
        && (long)frame.X + frame.Width <= info.Width
        && (long)frame.Y + frame.Height <= info.Height;

    private void Damage(int index, string why)
    {
        if (!_damage.TryAdd(index, why))
            return;

        Logger.Warning($"[{nameof(ApngFrameRenderer)}] {why}.");
        damaged?.Invoke(index, why);
    }

    private SKBitmap Copy(SKRectI rect)
    {
        using var subset = new SKBitmap();
        if (!_canvas!.ExtractSubset(subset, rect))
            throw new InvalidOperationException($"Could not read back {rect} of the canvas.");

        return subset.Copy() ?? throw new OutOfMemoryException($"Could not copy a {rect.Width}x{rect.Height} region.");
    }

    private SKBitmap Snapshot(SKBitmap canvas) =>
        canvas.Copy() ?? throw new OutOfMemoryException($"Could not copy a {info.Width}x{info.Height} frame.");

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

    private void DropUnderneath()
    {
        _underneath?.Dispose();
        _underneath = null;
    }

    protected override void Release()
    {
        _canvas?.Dispose();
        _canvas = null;
        _canvasFrame = -1;
        DropUnderneath();
    }
}