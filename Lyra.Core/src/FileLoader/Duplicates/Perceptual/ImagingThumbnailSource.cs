using Lyra.Imaging;

namespace Lyra.FileLoader.Duplicates.Perceptual;

/// <summary>Adapter over <see cref="GrayscaleThumbnail"/>.</summary>
public sealed class ImagingThumbnailSource : IThumbnailSource
{
    public byte[]? GetLuma(string path, int width, int height, CancellationToken ct = default)
        => GrayscaleThumbnail.Decode(path, width, height, ct);
}