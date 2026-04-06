using SkiaSharp;

namespace Lyra.UI.Theme;

public static class Palette
{
    // Backgrounds
    public static readonly SKColor Background = new(40, 40, 40);
    public static readonly SKColor Transparent = new(0, 0, 0, 0);

    // Text
    public static readonly SKColor Foreground = new(200, 200, 200);
    public static readonly SKColor SelectedForeground = new(255, 200, 21, 170);

    // Text on light backgrounds
    public static readonly SKColor ForegroundDark = new(30, 30, 30);

    // Default (secondary) button
    public static readonly SKColor Primary = new(85, 85, 85);
    public static readonly SKColor HoverPrimary = new(75, 75, 75);
    public static readonly SKColor PressedPrimary = new(65, 65, 65);

    public static readonly SKColor Disabled = new(0, 0, 0, 102);
    
    public static readonly SKColor Dim = new(140, 140, 140);

    // Accent (primary) button
    public static readonly SKColor Accent = new(240, 240, 240);
    public static readonly SKColor HoverAccent = new(220, 220, 220);
    public static readonly SKColor PressedAccent = new(200, 200, 200);

    // Danger
    public static readonly SKColor Danger = new(139, 35, 35);
    public static readonly SKColor HoverDanger = new(155, 40, 40);
    public static readonly SKColor PressedDanger = new(120, 30, 30);

    // Subtle — for Ghost, hover states on transparent components
    public static readonly SKColor Subtle = new(255, 255, 255, 10);
    public static readonly SKColor HoverSubtle = new(255, 255, 255, 30);
    public static readonly SKColor PressedSubtle = new(255, 255, 255, 40);

    // Borders
    public static readonly SKColor Border = new(100, 100, 100);
    public static readonly SKColor BorderActive = new(150, 150, 150);
    
    // Panels / surfaces
    public static readonly SKColor Panel = new(25, 25, 25, 220);
}