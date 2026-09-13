using SkiaSharp;

namespace Lyra.Imaging.Content.Tiling;

/// <summary>
/// A grid of textures covering one image, asked for the part of it that is on screen.
/// </summary>
public interface ITileSource : IDisposable
{
    IEnumerable<RasterTile> GetTiles(SKRect visibleFullRect, SKSize imageSize, float pixelsPerFullUnit);

    long ByteSize { get; }

    long VisibleByteSize(SKRect visibleFullRect, SKSize imageSize, float pixelsPerFullUnit);
}

public readonly record struct RasterTile(SKImage Image, SKRect DestRect);