using Lyra.Imaging.Interop;
using SkiaSharp;

namespace Lyra.Imaging.Decoding.Decoders.Animation;

/// <summary>A frame of a HEIF or AVIF sequence, and its alpha when that comes from a track of its own.</summary>
internal sealed class HeifFrame : IDisposable
{
    private readonly HeifImage _color;
    private readonly HeifImage? _alpha;

    private HeifFrame(HeifImage color, HeifImage? alpha)
    {
        _color = color;
        _alpha = alpha;
    }

    public static HeifFrame? DecodeNext(HeifTrack color, HeifTrack? alpha)
    {
        if (color.DecodeNextRgba() is not { } image)
            return null;

        if (alpha is null)
            return new HeifFrame(image, null);

        try
        {
            return new HeifFrame(image, alpha.DecodeNextAlpha() ?? throw new HeifException("The alpha track ends before the frame"));
        }
        catch
        {
            image.Dispose();
            throw;
        }
    }

    public SKBitmap ToBitmap(SKSizeI size, SKColorSpace? colorSpace, CancellationToken ct)
    {
        var bitmap = _color.CopyRgba(size.Width, size.Height, colorSpace, ct);

        try
        {
            _alpha?.CopyAlphaInto(bitmap);
        }
        catch
        {
            bitmap.Dispose();
            throw;
        }

        return bitmap;
    }

    public void Dispose()
    {
        _alpha?.Dispose();
        _color.Dispose();
    }
}