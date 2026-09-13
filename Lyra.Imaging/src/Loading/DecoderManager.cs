using Lyra.Common;
using Lyra.Imaging.Decoding;
using Lyra.Imaging.Decoding.Decoders;

namespace Lyra.Imaging.Loading;

internal static class DecoderManager
{
    private static readonly List<IImageDecoder> Decoders =
    [
        new SkiaDecoder(),
        new TgaDecoder(),
        new DdsDecoder(),
        new KtxDecoder(),
        new TiffDecoder(),
        new HeifDecoder(),
        new ExrDecoder(),
        new HdrDecoder(),
        new IcnsDecoder(),
        new IcoDecoder(),
        new PsdDecoder(),
        new SvgDecoder(),
        new J2KDecoder(),
        new JxlDecoder()
    ];

    public static IImageDecoder GetDecoder(ImageFormatType format)
    {
        return Decoders.FirstOrDefault(it => it.CanDecode(format), Decoders.First());
    }
}