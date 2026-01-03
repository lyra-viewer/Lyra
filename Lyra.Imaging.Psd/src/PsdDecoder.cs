using Lyra.Imaging.Psd.Parser;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.Metadata;
using SixLabors.ImageSharp.PixelFormats;

namespace Lyra.Imaging.Psd;

public sealed class PsdDecoder : SpecializedImageDecoder<PsdDecoderOptions>
{
    protected override ImageInfo Identify(DecoderOptions options, Stream stream, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(stream);

        var header = PsdDocument.ReadHeader(stream);

        return new ImageInfo(
            pixelType: new PixelTypeInfo(32), // Output format: RGBA8 (ImageSharp: Rgba32)
            size: new Size(header.Width, header.Height),
            metadata: new ImageMetadata()
        );
    }

    protected override Image<TPixel> Decode<TPixel>(PsdDecoderOptions options, Stream stream, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(stream);

        using var rgba = (Image<Rgba32>)Decode(options, stream, cancellationToken);
        return rgba.CloneAs<TPixel>();
    }

    protected override Image Decode(PsdDecoderOptions options, Stream stream, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(stream);

        var document = PsdDocument.ReadDocument(stream);
        return PsdCompositeImageExtractor.DecodeRgba32(document, stream, cancellationToken);
    }

    protected override PsdDecoderOptions CreateDefaultSpecializedOptions(DecoderOptions options) => new() { GeneralOptions = options };
}