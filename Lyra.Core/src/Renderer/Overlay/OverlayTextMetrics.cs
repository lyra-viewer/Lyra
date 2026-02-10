using SkiaSharp;

namespace Lyra.Renderer.Overlay;

public static class OverlayTextMetrics
{
    public const int BasePadding = 13;
    public const int BaseLineGap = 7;

    public static float Padding(float scale) => BasePadding * scale;

    public static float LineHeight(SKFont font, float scale) => font.Size + (BaseLineGap * scale);
}