using Lyra.Imaging.Content;
using Lyra.Imaging.Decoding.Support;
using SkiaSharp;

namespace Lyra.Imaging.Decoding.Decoders.Animation;

/// <summary>Frames a renderer draws as 8-bit bitmaps, published as raster content.</summary>
/// <param name="owner">Disposed with the source: the renderer and its canvas.</param>
internal sealed class RasterFrameSource(int count, Func<int, CancellationToken, SKBitmap> render, IDisposable owner, Composite composite) : IFrameSource
{
    public int Count => count;

    public ICompositeContent Decode(int index, CancellationToken ct) => RasterContentBuilder.Build(render(index, ct), composite);

    public void Dispose() => owner.Dispose();
}