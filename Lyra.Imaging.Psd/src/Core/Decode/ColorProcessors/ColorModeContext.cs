using Lyra.Imaging.Psd.Core.Common;
using Lyra.Imaging.Psd.Core.Decode.Pixel;

namespace Lyra.Imaging.Psd.Core.Decode.ColorProcessors;

public sealed record ColorModeContext(
    ColorMode ColorMode,
    SurfaceFormat OutputFormat,
    byte[]? IndexedPaletteRgb,     // 768 bytes if Indexed
    byte[]? IccProfile,            // from image resources if available
    bool PreferColorManagement     // runtime switch
    // other stuff: transparency index, etc.
);