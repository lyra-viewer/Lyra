namespace Lyra.Imaging.Psd.Parser.SectionData;

public readonly record struct ImageResourceBlockHeader(
    ushort Id,
    string Name,
    uint DataSize,
    long DataOffset
);