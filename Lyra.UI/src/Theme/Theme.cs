using SkiaSharp;

namespace Lyra.UI.Theme;

public sealed record Theme
{
    public required SKColor Background { get; init; }

    public required SKColor Foreground { get; init; }
    public required SKColor Muted { get; init; }
    public required SKColor Dim { get; init; }
    public required SKColor Faint { get; init; }

    public required SKColor SelectedForeground { get; init; }
    public required SKColor SelectedBackground { get; init; }
    public required SKColor ForegroundDark { get; init; }

    public required SKColor Primary { get; init; }
    public required SKColor HoverPrimary { get; init; }
    public required SKColor PressedPrimary { get; init; }

    public required SKColor Disabled { get; init; }

    public required SKColor Accent { get; init; }
    public required SKColor HoverAccent { get; init; }
    public required SKColor PressedAccent { get; init; }

    public required SKColor Danger { get; init; }
    public required SKColor HoverDanger { get; init; }
    public required SKColor PressedDanger { get; init; }

    public required SKColor Subtle { get; init; }
    public required SKColor HoverSubtle { get; init; }
    public required SKColor PressedSubtle { get; init; }

    public required SKColor Border { get; init; }
    public required SKColor BorderActive { get; init; }

    public required SKColor Panel { get; init; }
}