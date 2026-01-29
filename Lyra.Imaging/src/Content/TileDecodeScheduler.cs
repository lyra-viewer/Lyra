using SkiaSharp;

namespace Lyra.Imaging.Content;

/// <summary>
/// Produces a decode priority for tiled images. Currently, yields a band (tileY) order
/// derived from a spiral tile order around a focus point.
/// </summary>
public sealed class TileDecodeScheduler
{
    private readonly object _gate = new();

    // Full-image coordinates (0..W/H). If null, it defaults to image center.
    private SKRect? _focusFullRect;

    /// <summary>
    /// Updates the scheduler focus in full-image coordinates.
    /// For PSD, this should typically be the current visibleFullRect.
    /// </summary>
    public void UpdateFocus(SKRect focusFullRect) // TODO wire this up
    {
        lock (_gate)
            _focusFullRect = focusFullRect;
    }

    /// <summary>
    /// Clears focus so the scheduler will use image center as a fallback.
    /// </summary>
    public void ClearFocus() // TODO wire this up
    {
        lock (_gate)
            _focusFullRect = null;
    }

    /// <summary>
    /// Builds a band order (tileY order) that prioritizes tiles near the focus rect center.
    /// Returns a list of unique band indices in the desired decode order.
    /// </summary>
    public List<int> BuildBandOrder(int tilesX, int tilesY, int tileWidth, int tileHeight)
    {
        if (tilesX <= 0 || tilesY <= 0)
            return [];

        // Determine focus tile from focus rect center (or image center if not set)
        SKRect? focus;
        lock (_gate) focus = _focusFullRect;

        float cx, cy;
        if (focus is { } r && !r.IsEmpty)
        {
            cx = (r.Left + r.Right) * 0.5f;
            cy = (r.Top + r.Bottom) * 0.5f;
        }
        else
        {
            cx = (tilesX * tileWidth) * 0.5f;
            cy = (tilesY * tileHeight) * 0.5f;
        }

        var startX = Math.Clamp((int)MathF.Floor(cx / tileWidth), 0, tilesX - 1);
        var startY = Math.Clamp((int)MathF.Floor(cy / tileHeight), 0, tilesY - 1);

        // Spiral tiles -> unique band list (tileY)
        var bandOrder = new List<int>(tilesY);
        var seen = new bool[tilesY];

        foreach (var (_, ty) in TileOrder.SpiralWithin(startX, startY, tilesX, tilesY))
        {
            if (seen[ty])
                continue;

            seen[ty] = true;
            bandOrder.Add(ty);

            if (bandOrder.Count == tilesY)
                break;
        }

        return bandOrder;
    }
}
