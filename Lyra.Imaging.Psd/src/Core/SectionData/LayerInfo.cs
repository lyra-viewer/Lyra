namespace Lyra.Imaging.Psd.Core.SectionData;

public readonly record struct LayerInfo(
    long PayloadOffset,
    long PayloadLength,
    short LayerCount
)
{
    public bool HasMergedAlpha => LayerCount < 0;
}