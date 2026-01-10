using SkiaSharp;

namespace Lyra.Imaging.Psd.Adapters.SkiaSharp;

public abstract record SkiaCompositeResult : IDisposable
{
    public sealed record Single(SKImage Image) : SkiaCompositeResult
    {
        public override void Dispose() => Image.Dispose();
    }

    public sealed record Tiled(
        int Width,
        int Height,
        int TileWidth,
        int TileHeight,
        int TilesX,
        int TilesY,
        SKImage?[] Tiles) : SkiaCompositeResult
    {
        public override void Dispose()
        {
            foreach (var img in Tiles)
                img?.Dispose();
        }
    }

    public abstract void Dispose();
}