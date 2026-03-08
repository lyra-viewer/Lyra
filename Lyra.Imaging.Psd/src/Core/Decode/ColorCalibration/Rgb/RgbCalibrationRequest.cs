using Lyra.Imaging.Psd.Core.Common;

namespace Lyra.Imaging.Psd.Core.Decode.ColorCalibration.Rgb;

public sealed record RgbCalibrationRequest
(
    ColorMode SourceColorMode,
    byte[]? EmbeddedIccProfile,
    bool PreferColorManagement,
    int GridSize
);