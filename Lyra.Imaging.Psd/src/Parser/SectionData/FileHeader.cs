using Lyra.Imaging.Psd.Parser.Common;

namespace Lyra.Imaging.Psd.Parser.SectionData;

public readonly record struct FileHeader(
    ushort Version,
    ushort NumberOfChannels,
    int Width,
    int Height,
    ushort Depth,
    ColorMode ColorMode
);