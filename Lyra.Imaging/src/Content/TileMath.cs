namespace Lyra.Imaging.Content;

public static class TileMath
{
    public static (int x, int y) GetTileAtPoint(float px, float py, int tileW, int tileH, int tilesX, int tilesY)
    {
        var x = Math.Clamp((int)MathF.Floor(px / tileW), 0, tilesX - 1);
        var y = Math.Clamp((int)MathF.Floor(py / tileH), 0, tilesY - 1);
        return (x, y);
    }
}