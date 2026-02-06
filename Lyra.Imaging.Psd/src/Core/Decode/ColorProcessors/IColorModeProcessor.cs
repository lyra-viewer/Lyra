using Lyra.Imaging.Psd.Core.Decode.Pixel;
using Lyra.Imaging.Psd.Core.SectionData;

namespace Lyra.Imaging.Psd.Core.Decode.ColorProcessors;

public interface IColorModeProcessor
{
    /// <summary>
    /// Name of the ICC profile that was effectively used for color conversion/calibration,
    /// or null when no color management was applied (identity).
    /// </summary>
    string? IccProfileUsed { get; }

    /// <summary>
    // Takes decompressed planes + optional metadata (palette, ICC, etc.)
    // Produces RGBA8888 (or BGRA depending on renderer)
    /// </summary>
    RgbaSurface Process(PlaneImage src, ColorModeContext ctx, ColorModeData? colorModeData, CancellationToken ct);
}