using Lyra.Common;
using Lyra.Imaging.Decoding.Support;
using Lyra.ManagedCodecs.Raster.Hdr;

namespace Lyra.Imaging.Decoding.Decoders;

internal sealed class HdrDecoder : FloatRgbaDecoderBase
{
    public override IReadOnlyList<ImageFormatType> Formats { get; } = [ImageFormatType.Hdr];

    public override DecoderCapabilities Capabilities => DecoderCapabilities.ReadsFromMemory;

    protected override FloatImageBuffer LoadPixels(DecodeContext context, CancellationToken ct)
    {
        var data = context.ReadAllBytes(ct);

        var image = RadianceHdrReader.Decode(data);
        return FloatImageBuffer.FromManaged(image.Pixels, image.Width, image.Height);
    }
}
