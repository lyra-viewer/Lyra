using Lyra.Common;
using Lyra.Imaging.Codecs;

namespace Lyra.Imaging.Pipeline;

internal static class DecoderManager
{
    private static readonly List<IImageDecoder> Decoders =
    [
        new SkiaDecoder(),
        new ImageSharpDecoder(),
        new HeifDecoder(),
        new ExrDecoder(),
        new HdrDecoder(),
        new PsdDecoder(),
        new SvgDecoder(),
        new J2KDecoder()
    ];

    public static IImageDecoder GetDecoder(ImageFormatType format)
    {
        return Decoders.FirstOrDefault(it => it.CanDecode(format), Decoders.First());
    }
}