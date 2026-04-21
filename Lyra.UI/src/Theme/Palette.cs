using SkiaSharp;

namespace Lyra.UI.Theme;

public static class Palette
{
    // Backgrounds
    public static readonly SKColor Background = new(38, 40, 43);
    public static readonly SKColor Transparent = new(0, 0, 0, 0);

    // Text
    public static readonly SKColor Foreground = new(205, 208, 212);
    public static readonly SKColor Muted = new(165, 168, 172);
    public static readonly SKColor Dim = new(135, 138, 143);

    // Solid amber accent
    public static readonly SKColor SelectedForeground = new(235, 175, 70);

    // Selection / pick highlight
    public static readonly SKColor SelectedBackground = new(255, 255, 255, 25);

    // Text on light backgrounds
    public static readonly SKColor ForegroundDark = new(28, 30, 33);

    // Default (secondary) button — hover/pressed go DARKER (preserved convention)
    public static readonly SKColor Primary = new(82, 86, 92);
    public static readonly SKColor HoverPrimary = new(72, 76, 82);
    public static readonly SKColor PressedPrimary = new(62, 66, 72);

    public static readonly SKColor Disabled = new(0, 0, 0, 102);

    // Accent (primary) button
    public static readonly SKColor Accent = new(238, 240, 243);
    public static readonly SKColor HoverAccent = new(218, 220, 223);
    public static readonly SKColor PressedAccent = new(198, 200, 203);

    // Danger
    public static readonly SKColor Danger = new(155, 45, 45);
    public static readonly SKColor HoverDanger = new(170, 55, 55);
    public static readonly SKColor PressedDanger = new(135, 38, 38);

    // Subtle
    public static readonly SKColor Subtle = new(255, 255, 255, 10);
    public static readonly SKColor HoverSubtle = new(255, 255, 255, 30);
    public static readonly SKColor PressedSubtle = new(255, 255, 255, 40);

    // Borders
    public static readonly SKColor Border = new(255, 255, 255, 25);
    public static readonly SKColor BorderActive = new(255, 255, 255, 60);

    // Panels / surfaces
    public static readonly SKColor Panel = new(22, 24, 27, 220);
}