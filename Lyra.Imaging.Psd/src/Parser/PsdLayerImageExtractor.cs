using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Lyra.Imaging.Psd.Parser;

public sealed class PsdLayerImageExtractor
{
    public Image<Rgba32>[] DecodeRgba32Layers(PsdDocument psdDocument, Stream stream, CancellationToken cancellationToken)
    {
        throw new NotImplementedException();
    }

    public Image<Rgba32> DecodeRgba32FromLayers(PsdDocument psdDocument, Stream stream, CancellationToken cancellationToken)
    {
        throw new NotImplementedException();
    }
}