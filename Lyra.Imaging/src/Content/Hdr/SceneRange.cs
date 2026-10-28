namespace Lyra.Imaging.Content.Hdr;

/// <summary>
/// Where a scene-referred image's highlights top out, when the content knows. A camera raw does,
/// because its sensor clips at a known level. Every other format gets the default, which leaves
/// the tone curve and the display's headroom alone.
/// </summary>
/// <param name="SceneWhite">The scene value that should map to white; for a raw, its saturation level.</param>
/// <param name="Peak">How far above SDR white the highlights may go on a display with headroom.</param>
/// <param name="Curve">The camera's own tone curve, used instead of the default.</param>
public readonly record struct SceneRange(float? SceneWhite = null, float? Peak = null, CameraToneCurve? Curve = null);
