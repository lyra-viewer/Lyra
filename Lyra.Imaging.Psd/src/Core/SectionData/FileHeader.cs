using Lyra.Imaging.Psd.Core.Common;

namespace Lyra.Imaging.Psd.Core.SectionData;

public readonly record struct FileHeader(
    ushort Version,
    ushort NumberOfChannels,
    int Width,
    int Height,
    ushort Depth,
    ColorMode ColorMode
);