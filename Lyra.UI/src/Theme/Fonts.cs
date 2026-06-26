using System.Runtime.InteropServices;
using SkiaSharp;

namespace Lyra.UI.Theme;

/// <summary>
/// Resolves a guaranteed-monospace font family for the current OS, computed once at startup.
/// </summary>
public static class Fonts
{
    /// <summary>Resolved monospace family name for the current platform.</summary>
    public static string MonospaceFamily { get; } = ResolveMonospace();

    private static string ResolveMonospace()
    {
        // Prioritized per-OS candidates. The first entry is the platform's conventional default;
        // later entries cover systems where it is absent. "Courier New" is a near-universal backstop.
        string[] candidates = RuntimeInformation.IsOSPlatform(OSPlatform.OSX)
            ? ["Menlo", "Monaco", "Courier New"]
            : RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                ? ["Consolas", "Cascadia Mono", "Courier New"]
                : ["DejaVu Sans Mono", "Liberation Mono", "Noto Sans Mono", "Ubuntu Mono", "Courier New"];

        foreach (var family in candidates)
        {
            using var tf = SKTypeface.FromFamilyName(family, SKFontStyle.Normal);
            // FamilyName must match (else FromFamilyName fell back) AND the face must be fixed-width.
            if (tf is not null && string.Equals(tf.FamilyName, family, StringComparison.OrdinalIgnoreCase)
                               && IsMonospace(tf))
                return family;
        }

        // None of the named candidates resolved. On Linux, fontconfig maps the generic alias
        // "monospace" to a real fixed-width face - use its actual family name if it is one.
        using var generic = SKTypeface.FromFamilyName("monospace", SKFontStyle.Normal);
        if (generic is not null && IsMonospace(generic))
            return generic.FamilyName;

        // Absolute last resort: hand back the conventional name and let Skia substitute. On any
        // system capable of running this GUI a monospace font is present.
        return candidates[0];
    }

    /// <summary>True when the typeface is fixed-width (a narrow and a wide glyph share an advance).</summary>
    private static bool IsMonospace(SKTypeface typeface)
    {
        using var font = new SKFont(typeface, 16f);
        using var paint = new SKPaint();
        var narrow = font.MeasureText("i", paint);
        var wide = font.MeasureText("W", paint);
        return Math.Abs(narrow - wide) < 0.01f;
    }
}
