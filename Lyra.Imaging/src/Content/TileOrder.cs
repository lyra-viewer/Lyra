namespace Lyra.Imaging.Content;

public static class TileOrder
{
    public static IEnumerable<(int x, int y)> SpiralWithin(int startX, int startY, int tilesX, int tilesY)
    {
        if (tilesX <= 0 || tilesY <= 0)
            yield break;

        startX = Math.Clamp(startX, 0, tilesX - 1);
        startY = Math.Clamp(startY, 0, tilesY - 1);

        var visited = new bool[tilesX * tilesY];
        var remaining = tilesX * tilesY;

        foreach (var (x, y) in SpiralFromInfinite(startX, startY))
        {
            if ((uint)x >= (uint)tilesX || (uint)y >= (uint)tilesY)
                continue;

            var idx = y * tilesX + x;
            if (visited[idx])
                continue;

            visited[idx] = true;
            remaining--;
            yield return (x, y);

            if (remaining == 0)
                yield break;
        }
    }

    private static IEnumerable<(int x, int y)> SpiralFromInfinite(int startX, int startY)
    {
        yield return (startX, startY);

        var x = startX;
        var y = startY;

        var step = 1;
        while (true)
        {
            for (var i = 0; i < step; i++) yield return (++x, y);
            for (var i = 0; i < step; i++) yield return (x, ++y);

            step++;

            for (var i = 0; i < step; i++) yield return (--x, y);
            for (var i = 0; i < step; i++) yield return (x, --y);

            step++;
        }
    }
}