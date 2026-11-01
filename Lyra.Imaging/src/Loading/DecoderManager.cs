using System.Collections.Frozen;
using Lyra.Common;
using Lyra.Common.SystemExtensions;
using Lyra.Imaging.Content.Status;
using Lyra.Imaging.Decoding;
using Lyra.Imaging.Decoding.Decoders;

namespace Lyra.Imaging.Loading;

internal static class DecoderManager
{
    private static readonly IImageDecoder[] Decoders =
    [
        new SkiaDecoder(),
        new GifDecoder(),
        new WebpDecoder(),
        new PngDecoder(),
        new TgaDecoder(),
        new QoiDecoder(),
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
        new JxlDecoder(),
        new RawDecoder(),
        new ModelDecoder()
    ];

    private static readonly FrozenDictionary<ImageFormatType, IImageDecoder> ByFormat = Index(Decoders);

    public static IReadOnlyCollection<ImageFormatType> Formats => ByFormat.Keys;

    internal static FrozenDictionary<ImageFormatType, IImageDecoder> Index(IEnumerable<IImageDecoder> decoders)
    {
        var byFormat = new Dictionary<ImageFormatType, IImageDecoder>();

        foreach (var decoder in decoders)
        {
            foreach (var format in decoder.Formats)
            {
                if (!byFormat.TryAdd(format, decoder))
                    throw new InvalidOperationException($"{format} is claimed by both {byFormat[format].GetType().Name} and {decoder.GetType().Name}.");
            }
        }

        return byFormat.ToFrozenDictionary();
    }

    public static IImageDecoder? Find(ImageFormatType format) => ByFormat.GetValueOrDefault(format);

    public static IImageDecoder GetDecoder(ImageFormatType format) =>
        Find(format) ?? throw new LoadFailureException(LoadFailureKind.DecodeFailed, format == ImageFormatType.Unknown ? "The image's format is not recognised." : $"{format.Description()} images cannot be decoded.");

    public static bool Can(ImageFormatType format, DecoderCapabilities capability) => Find(format) is { } decoder && decoder.Capabilities.HasFlag(capability);
}