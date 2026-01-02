namespace Lyra.Imaging.Psd.Parser.SectionData;

public readonly record struct GlobalLayerMaskSummary(
    long PayloadOffset,
    long PayloadLength
);