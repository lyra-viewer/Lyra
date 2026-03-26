using SkiaSharp;

namespace Lyra.Renderer.Overlay;

public static class OverlayTextMetrics
{
    public const int BasePadding = 13;
    public const int BaseLineGap = 7;

    public static float Padding() => BasePadding;

    public static float LineHeight(SKFont font) => font.Size + BaseLineGap;
}