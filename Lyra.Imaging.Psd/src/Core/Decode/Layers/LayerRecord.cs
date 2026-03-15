namespace Lyra.Imaging.Psd.Core.Decode.Layers;

/// <summary>
/// Decoded layer summary containing the resolved name and basic geometry.
/// </summary>
public readonly record struct LayerRecord(
    int Top,
    int Left,
    int Bottom,
    int Right,
    string Name
)
{
    public int Width => Right - Left;
    public int Height => Bottom - Top;
}