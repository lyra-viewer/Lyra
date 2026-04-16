using Lyra.UI.SupportingTypes;
using SkiaSharp;

namespace Lyra.UI.Components.Layout;

public class VStack : StackBase
{
    protected override float GetMain(SKSize size) => size.Height;
    protected override float GetCross(SKSize size) => size.Width;
    protected override SKSize MakeSize(float main, float cross) => new(cross, main);

    protected override SKRect MakeChildRect(
        float mainOffset, float crossOffset,
        float mainLength, float crossLength,
        SKRect bounds) => new(
        bounds.Left + crossOffset,
        bounds.Top + mainOffset,
        bounds.Left + crossOffset + crossLength,
        bounds.Top + mainOffset + mainLength);

    protected override SizeMode GetMainSizeMode(IComponent c) => c.VerticalSize;
    protected override SizeMode GetCrossSizeMode(IComponent c) => c.HorizontalSize;

    protected override float GetCrossOffset(IComponent child, float childCross, float availableCross)
        => child.HorizontalAlign switch
        {
            HAlign.Center => (availableCross - childCross) / 2f,
            HAlign.Right => availableCross - childCross,
            _ => 0f
        };

    protected override float? GetMainMaxSize(IComponent child) => child.MaxHeight;
}