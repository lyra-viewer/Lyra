using Lyra.Common;
using Lyra.UI.Theme;
using SkiaSharp;

namespace Lyra.Renderer;

public static class BundledFonts
{
    public const string MonospaceFamily = "JetBrains Mono";

    private const string RegularResource = "LyraViewer.Fonts.JetBrainsMono-Regular.ttf";
    private const string BoldResource = "LyraViewer.Fonts.JetBrainsMono-Bold.ttf";

    private static readonly Lock RegisterLock = new();
    private static bool _registered;

    public static void Register()
    {
        lock (RegisterLock)
        {
            if (_registered)
                return;

            RegisterFaces();
            _registered = true;
        }
    }

    private static void RegisterFaces()
    {
        var regular = Load(RegularResource);
        if (regular is null)
        {
            Logger.Warning($"[BundledFonts] '{MonospaceFamily}' regular face unavailable; using the platform monospace font.");
            return;
        }

        Fonts.Register(MonospaceFamily, SKFontStyleWeight.Normal, SKFontStyleSlant.Upright, regular);

        var bold = Load(BoldResource);
        if (bold is not null)
            Fonts.Register(MonospaceFamily, SKFontStyleWeight.Bold, SKFontStyleSlant.Upright, bold);
        else
            Logger.Warning($"[BundledFonts] '{MonospaceFamily}' bold face unavailable; bold text falls back to the platform font.");

        Fonts.SetDefaultMonospace(MonospaceFamily);
        Logger.Info($"[BundledFonts] Registered bundled '{MonospaceFamily}'.");
    }

    private static SKTypeface? Load(string resourceName)
    {
        try
        {
            using var stream = typeof(BundledFonts).Assembly.GetManifestResourceStream(resourceName);

            if (stream is null)
            {
                Logger.Warning($"[BundledFonts] Embedded font resource not found: {resourceName}");
                return null;
            }

            // SKTypeface.FromStream takes ownership of the stream contents, and the
            // typeface itself is kept for the life of the process by the registry.
            var typeface = SKTypeface.FromStream(stream);

            if (typeface is null)
                Logger.Warning($"[BundledFonts] Failed to parse embedded font: {resourceName}");

            return typeface;
        }
        catch (Exception ex)
        {
            Logger.Warning($"[BundledFonts] Error loading embedded font '{resourceName}': {ex.Message}");
            return null;
        }
    }
}