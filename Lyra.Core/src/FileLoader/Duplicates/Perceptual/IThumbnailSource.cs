namespace Lyra.FileLoader.Duplicates.Perceptual;

/// <summary>
/// Supplies a small grayscale (luma) buffer for an image. Abstracted so the perceptual
/// scanner can be unit-tested with synthetic pixels.
/// </summary>
/// <remarks>Implementations must be callable concurrently: the perceptual scan hashes in parallel.</remarks>
public interface IThumbnailSource
{
    byte[]? GetLuma(string path, int width, int height, CancellationToken ct = default);
}
