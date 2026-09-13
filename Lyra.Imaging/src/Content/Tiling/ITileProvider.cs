using SkiaSharp;

namespace Lyra.Imaging.Content.Tiling;

public interface ITileProvider : IDisposable
{
    SKImage? Decode(int level, int tileX, int tileY, CancellationToken ct);
}