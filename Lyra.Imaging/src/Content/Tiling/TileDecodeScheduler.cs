namespace Lyra.Imaging.Content.Tiling;

/// <summary>
/// The order a tiled image's bands are decoded in: outward from the middle, so the part of a
/// sheet most likely to be looked at resolves first.
/// </summary>
internal static class TileDecodeScheduler
{
    /// <summary>
    /// A band (tileY) order that starts at the middle of the grid and works outward. Each band
    /// appears once, in the order the spiral first reaches it.
    /// </summary>
    public static List<int> BuildBandOrder(int tilesX, int tilesY)
    {
        if (tilesX <= 0 || tilesY <= 0)
            return [];

        var bandOrder = new List<int>(tilesY);
        var seen = new bool[tilesY];

        foreach (var (_, ty) in TileOrder.SpiralWithin(tilesX / 2, tilesY / 2, tilesX, tilesY))
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