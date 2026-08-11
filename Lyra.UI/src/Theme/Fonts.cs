using System.Runtime.InteropServices;
using SkiaSharp;

namespace Lyra.UI.Theme;

public static class Fonts
{
    // Registered typefaces are process-lifetime and deliberately never disposed:
    // SKFont instances built from them outlive any single lookup, and disposing a
    // typeface still referenced by a font is a crash rather than a fallback.
    private static readonly Dictionary<(string Family, SKFontStyleWeight Weight, SKFontStyleSlant Slant), SKTypeface> Registered = [];
    private static readonly Lock RegisteredLock = new();

    private static string? _preferredMonospace;
    private static readonly string SystemMonospace = ResolveMonospace();

    /// <summary>
    /// Monospace family used by default. This is the platform's font unless the
    /// host has nominated one of its own via <see cref="SetDefaultMonospace"/>.
    /// </summary>
    public static string MonospaceFamily => _preferredMonospace ?? SystemMonospace;

    /// <summary>
    /// Registers a typeface the application ships itself; anything not registered falls
    /// through to the platform search, which degrades the style rather than
    /// failing.
    /// </summary>
    public static void Register(string family, SKFontStyleWeight weight, SKFontStyleSlant slant, SKTypeface typeface)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(family);
        ArgumentNullException.ThrowIfNull(typeface);

        lock (RegisteredLock)
            Registered[(family, weight, slant)] = typeface;
    }

    /// <summary>
    /// Nominates the default monospace family, normally one just registered.
    /// </summary>
    public static void SetDefaultMonospace(string family)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(family);
        _preferredMonospace = family;
    }

    /// <summary>
    /// The typeface for a family and style - registered first, platform second.
    /// Never null: Skia substitutes a default rather than failing.
    /// </summary>
    public static SKTypeface Resolve(string family, SKFontStyleWeight weight, SKFontStyleSlant slant)
    {
        lock (RegisteredLock)
        {
            if (Registered.TryGetValue((family, weight, slant), out var registered))
                return registered;
        }

        return SKTypeface.FromFamilyName(family, new SKFontStyle(weight, SKFontStyleWidth.Normal, slant));
    }

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
