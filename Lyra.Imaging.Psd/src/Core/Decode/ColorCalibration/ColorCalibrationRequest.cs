using Lyra.Imaging.Psd.Core.Common;

namespace Lyra.Imaging.Psd.Core.Decode.ColorCalibration;

public sealed record ColorCalibrationRequest
(
    ColorMode SourceColorMode,
    byte[]? EmbeddedIccProfile,
    bool PreferColorManagement,
    int GridSize
);