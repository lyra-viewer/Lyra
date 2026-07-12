namespace Lyra.SdlCore;

/// <param name="Scale">Pixel density (framebuffer px ÷ logical points) — for geometry/centering.</param>
/// <param name="ContentScale">UI content scale (SDL display scale) — for sizing UI chrome. Equals
/// scale on platforms like macOS, but diverges on Linux/X11 with desktop scaling.</param>
public readonly record struct PixelSize(int PixelWidth, int PixelHeight, float Scale, float ContentScale);