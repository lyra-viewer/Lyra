using SkiaSharp;

namespace Lyra.Renderer.Drawing;

/// <summary>
/// What the surface being drawn into is in color terms: the space its values live in, and whether
/// it can carry light above SDR white. SDR white is 1.0 in every such space.
/// </summary>
public readonly record struct SurfaceProfile(SKColorSpace? ColorSpace, bool IsExtendedRange, float Headroom = 1f)
{
    public const float SdrWhite = 1f;
    
    public float Ceiling => IsExtendedRange ? MathF.Max(Headroom, SdrWhite) : SdrWhite;

    public static SurfaceProfile DisplayReferred(SKColorSpace? colorSpace) => new(colorSpace, false);
    
    public static SurfaceProfile Extended(SKColorSpace? colorSpace, float headroom) => new(colorSpace, true, headroom);
    
    public static readonly SurfaceProfile Unknown = new(null, false);
}
