using Lyra.Common;
using Lyra.Imaging.Content;
using Lyra.Imaging.Decoding.Support;
using Lyra.ManagedCodecs.Raster.Hdr;

namespace Lyra.Imaging.Decoding.Decoders;

internal sealed class HdrDecoder : FloatRgbaDecoderBase
{
    public override bool CanDecode(ImageFormatType format) => format == ImageFormatType.Hdr;

    protected override FloatImageBuffer LoadPixels(Composite composite, CancellationToken ct)
    {
        var data = DecoderIO.ReadAllBytes(composite.FileInfo.FullName, ct, out var readMs, composite.ReportTransferred);
        composite.CompleteTransfer(data.Length, readMs);

        var image = RadianceHdrReader.Decode(data);
        return FloatImageBuffer.FromManaged(image.Pixels, image.Width, image.Height);
    }
}
