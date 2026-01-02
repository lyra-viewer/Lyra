namespace Lyra.Imaging.Psd.Parser.SectionData;

public readonly record struct ImageResources(
    uint Length,
    ImageResourceBlockHeader[] Blocks
);