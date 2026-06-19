using Lyra.Common;
using Lyra.Imaging.Decoding.Support;
using Lyra.ManagedCodecs.Raster.Hdr;

namespace Lyra.Imaging.Decoding.Decoders;

internal sealed class HdrDecoder : FloatRgbaDecoderBase
{
    public override bool CanDecode(ImageFormatType format) => format == ImageFormatType.Hdr;

    protected override FloatImageBuffer LoadPixels(string path)
    {
        var image = RadianceHdrReader.Decode(File.ReadAllBytes(path));
        return FloatImageBuffer.FromManaged(image.Pixels, image.Width, image.Height);
    }
}
