namespace Lyra.Imaging.Psd.Core.SectionData;

public readonly record struct ImageResourceBlockHeader(
    ushort Id,
    string Name,
    uint DataSize,
    long DataOffset
);