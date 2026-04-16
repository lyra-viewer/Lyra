using Lyra.UI.SupportingTypes;
using SkiaSharp;

namespace Lyra.UI.Components.Layout;

public class HStack : StackBase
{
    protected override float GetMain(SKSize size) => size.Width;
    protected override float GetCross(SKSize size) => size.Height;
    protected override SKSize MakeSize(float main, float cross) => new(main, cross);

    protected override SKRect MakeChildRect(
        float mainOffset, float crossOffset,
        float mainLength, float crossLength,
        SKRect bounds) => new(
        bounds.Left + mainOffset,
        bounds.Top + crossOffset,
        bounds.Left + mainOffset + mainLength,
        bounds.Top + crossOffset + crossLength);

    protected override SizeMode GetMainSizeMode(IComponent c) => c.HorizontalSize;
    protected override SizeMode GetCrossSizeMode(IComponent c) => c.VerticalSize;

    protected override float GetCrossOffset(IComponent child, float childCross, float availableCross)
        => child.VerticalAlign switch
        {
            VAlign.Center => (availableCross - childCross) / 2f,
            VAlign.Bottom => availableCross - childCross,
            _ => 0f
        };

    protected override float? GetMainMaxSize(IComponent child) => child.MaxWidth;
}