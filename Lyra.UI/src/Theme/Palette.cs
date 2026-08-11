using SkiaSharp;

namespace Lyra.UI.Theme;

public static class Palette
{
    public static readonly SKColor Transparent = new(0, 0, 0, 0);
    
    public static readonly Theme Default = new()
    {
        Background          = new SKColor(38, 40, 43),

        Foreground          = new SKColor(205, 208, 212),
        Muted               = new SKColor(165, 168, 172),
        Dim                 = new SKColor(135, 138, 143),
        Faint               = new SKColor(100, 103, 108),

        SelectedForeground  = new SKColor(235, 175, 70),
        SelectedBackground  = new SKColor(255, 255, 255, 25),
        ForegroundDark      = new SKColor(28, 30, 33),

        Primary             = new SKColor(82, 86, 92),
        HoverPrimary        = new SKColor(72, 76, 82),
        PressedPrimary      = new SKColor(62, 66, 72),

        Disabled            = new SKColor(0, 0, 0, 102),

        Accent              = new SKColor(238, 240, 243),
        HoverAccent         = new SKColor(218, 220, 223),
        PressedAccent       = new SKColor(198, 200, 203),

        Danger              = new SKColor(155, 45, 45),
        HoverDanger         = new SKColor(170, 55, 55),
        PressedDanger       = new SKColor(135, 38, 38),

        Subtle              = new SKColor(255, 255, 255, 10),
        HoverSubtle         = new SKColor(255, 255, 255, 30),
        PressedSubtle       = new SKColor(255, 255, 255, 40),

        Border              = new SKColor(255, 255, 255, 25),
        BorderActive        = new SKColor(255, 255, 255, 60),

        Panel               = new SKColor(22, 24, 27, 220),
    };

    private static Theme _current = Default;

    public static void Apply(Theme theme) => _current = theme;

    // Backgrounds
    public static SKColor Background => _current.Background;

    // Text
    public static SKColor Foreground => _current.Foreground;
    public static SKColor Muted => _current.Muted;
    public static SKColor Dim => _current.Dim;
    public static SKColor Faint => _current.Faint;

    // Solid amber accent
    public static SKColor SelectedForeground => _current.SelectedForeground;

    // Selection / pick highlight
    public static SKColor SelectedBackground => _current.SelectedBackground;

    // Text on light backgrounds
    public static SKColor ForegroundDark => _current.ForegroundDark;

    // Default (secondary) button — hover/pressed go DARKER (preserved convention)
    public static SKColor Primary => _current.Primary;
    public static SKColor HoverPrimary => _current.HoverPrimary;
    public static SKColor PressedPrimary => _current.PressedPrimary;

    public static SKColor Disabled => _current.Disabled;

    // Accent (primary) button
    public static SKColor Accent => _current.Accent;
    public static SKColor HoverAccent => _current.HoverAccent;
    public static SKColor PressedAccent => _current.PressedAccent;

    // Danger
    public static SKColor Danger => _current.Danger;
    public static SKColor HoverDanger => _current.HoverDanger;
    public static SKColor PressedDanger => _current.PressedDanger;

    // Subtle
    public static SKColor Subtle => _current.Subtle;
    public static SKColor HoverSubtle => _current.HoverSubtle;
    public static SKColor PressedSubtle => _current.PressedSubtle;

    // Borders
    public static SKColor Border => _current.Border;
    public static SKColor BorderActive => _current.BorderActive;

    // Panels / surfaces
    public static SKColor Panel => _current.Panel;
}