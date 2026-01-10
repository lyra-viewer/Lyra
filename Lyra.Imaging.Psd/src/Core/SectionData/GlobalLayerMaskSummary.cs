namespace Lyra.Imaging.Psd.Core.SectionData;

public readonly record struct GlobalLayerMaskSummary(
    long PayloadOffset,
    long PayloadLength
);