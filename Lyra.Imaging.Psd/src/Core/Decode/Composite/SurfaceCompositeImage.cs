using Lyra.Imaging.Psd.Core.Decode.Pixel;

namespace Lyra.Imaging.Psd.Core.Decode.Composite;

public sealed class SurfaceCompositeImage(RgbaSurface surface, SurfaceFormat format) : ICompositeImage
{
    public RgbaSurface Surface { get; } = surface ?? throw new ArgumentNullException(nameof(surface));

    public int Width => Surface.Width;
    public int Height => Surface.Height;
    public SurfaceFormat Format { get; } = format;

    public void Dispose() => Surface.Dispose();
}