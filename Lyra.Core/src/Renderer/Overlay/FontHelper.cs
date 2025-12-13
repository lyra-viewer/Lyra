using Lyra.SystemUtils;
using SkiaSharp;

namespace Lyra.Renderer.Overlay;

public static class FontHelper
{
    public static SKFont GetScaledMonoFont(float baseSize, float scale)
    {
        var path = TtfLoader.GetMonospaceFontPath();
        var tf = SKTypeface.FromFile(path);
        return new SKFont(tf, baseSize * scale);
    }
}