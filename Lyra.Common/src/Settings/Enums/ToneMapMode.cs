using System.ComponentModel;

namespace Lyra.Common.Settings.Enums;

/// <summary>
/// How scene-referred HDR pixels (EXR, Radiance HDR, HDR JXL, BC6H) are brought down to display
/// values.
///
/// Applied in a shader as the image is drawn, so changing it is a uniform update on the next
/// frame rather than a re-decode. The exception is an image too large to hold as half-float,
/// where the curve is baked in at decode instead - those get no controls, precisely because
/// changing this could not reach them.
/// </summary>
public enum ToneMapMode
{
    /// <summary>
    /// ACES filmic (Narkowicz 2015). Rolls highlights off smoothly and keeps a photographic
    /// look, at the cost of compressing very bright sources - the curve saturates near 5.0, so
    /// a sun sitting in an already-bright sky flattens into it.
    /// </summary>
    [Description("ACES filmic")]
    Aces,

    /// <summary>
    /// Reinhard extended, with the white point taken from the image itself. Spends the top of
    /// the range on whatever the brightest thing actually is, so suns and specular hits stay
    /// separated from their surroundings.
    /// </summary>
    [Description("Reinhard extended")]
    ReinhardExtended,

    /// <summary>
    /// No curve: scale, clip at 1.0, encode. The plain SDR rendering. Blows out bright sources
    /// into solid, obvious shapes.
    /// </summary>
    [Description("Clip")]
    Clip
}