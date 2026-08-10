using SkiaSharp;

namespace Lyra.UI.Components;

public static class InputExtensions
{
    public static T OnPointerDown<T>(this T c, Action<SKPoint> handler) where T : ComponentBase
    {
        c.PointerDown += handler;
        return c;
    }

    public static T OnPointerUp<T>(this T c, Action<SKPoint> handler) where T : ComponentBase
    {
        c.PointerUp += handler;
        return c;
    }

    public static T OnPointerMove<T>(this T c, Action<SKPoint> handler) where T : ComponentBase
    {
        c.PointerMove += handler;
        return c;
    }

    public static T OnPointerEnter<T>(this T c, Action handler) where T : ComponentBase
    {
        c.PointerEnter += handler;
        return c;
    }

    public static T OnPointerLeave<T>(this T c, Action handler) where T : ComponentBase
    {
        c.PointerLeave += handler;
        return c;
    }

    public static T OnClick<T>(this T c, Action handler) where T : Controls.Button.Button
    {
        c.Click += handler;
        return c;
    }
}
