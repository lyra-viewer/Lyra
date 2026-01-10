using Lyra.Imaging.Psd.Core.Decode.Pixel;

namespace Lyra.Imaging.Psd.Core.Decode.Composite;

public interface ICompositeImage : IDisposable
{
    int Width { get; }
    int Height { get; }
    SurfaceFormat Format { get; }
}