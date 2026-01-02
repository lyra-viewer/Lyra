namespace Lyra.Imaging.Psd.Parser.SectionData;

public readonly record struct LayerAndMaskInformation(
    long SectionLength,
    LayerInfo LayerInfo,
    GlobalLayerMaskSummary GlobalLayerMask,
    AdditionalLayerInformation[] AdditionalInfo
)
{
    public static LayerAndMaskInformation Empty { get; } =
        new(0, default, default, Array.Empty<AdditionalLayerInformation>());
}