using System.Buffers;
using System.Globalization;
using System.Text;
using SkiaSharp;

namespace Lyra.UI.Theme;

/// <summary>A slice of text drawn with one font. The slice is copied once at layout, not per frame.</summary>
internal readonly record struct TextRun(string Text, SKFont Font, float Width);

/// <remarks>
/// Fallback typefaces and fonts are cached for the process lifetime and never
/// disposed, for the same reason as <see cref="Fonts"/>' registry: fonts built
/// from them outlive any lookup.
/// </remarks>
internal static class FontFallback
{
    private static readonly Lock CacheLock = new();

    // Null value: nothing on the system has the character; the primary draws it as .notdef.
    private static readonly Dictionary<CharKey, SKTypeface?> Typefaces = [];
    private static readonly Dictionary<FaceKey, SKTypeface> Faces = [];
    private static readonly Dictionary<FaceSizeKey, SKFont> FontsBySize = [];

    private readonly record struct CharKey(int CodePoint, SKFontStyleWeight Weight, SKFontStyleSlant Slant);
    private readonly record struct FaceKey(string Family, int Weight, SKFontStyleSlant Slant);
    private readonly record struct FaceSizeKey(FaceKey Face, float Size);
    
    public static float Layout(ReadOnlySpan<char> text, SKFont primary, List<TextRun>? runs)
    {
        runs?.Clear();

        if (text.IsEmpty)
            return 0f;

        var glyphs = primary.GetGlyphs(text);

        if (Array.IndexOf(glyphs, (ushort)0) < 0)
        {
            var width = primary.MeasureText(text);
            runs?.Add(new TextRun(text.ToString(), primary, width));
            return width;
        }

        var total = 0f;
        var runStart = 0;
        SKFont? runFont = null;
        var index = 0;
        var glyph = 0;

        while (index < text.Length)
        {
            if (Rune.DecodeFromUtf16(text[index..], out var rune, out var consumed) != OperationStatus.Done)
                rune = Rune.ReplacementChar;
            
            var font = glyph < glyphs.Length && glyphs[glyph] != 0
                ? primary
                : Attaches(rune) && runFont is not null && !ReferenceEquals(runFont, primary)
                    ? runFont
                    : FontFor(rune.Value, primary);

            if (runFont is not null && !ReferenceEquals(font, runFont))
            {
                total += Flush(text, runStart, index - runStart, runFont, runs);
                runStart = index;
            }

            runFont = font;
            index += consumed;
            glyph++;
        }

        if (runFont is not null)
            total += Flush(text, runStart, index - runStart, runFont, runs);

        return total;
    }

    public static void Draw(SKCanvas canvas, List<TextRun> runs, float x, float y, SKPaint paint)
    {
        foreach (var run in runs)
        {
            canvas.DrawText(run.Text, x, y, SKTextAlign.Left, run.Font, paint);
            x += run.Width;
        }
    }

    private static float Flush(ReadOnlySpan<char> text, int start, int length, SKFont font, List<TextRun>? runs)
    {
        var slice = text.Slice(start, length);
        var width = font.MeasureText(slice);
        runs?.Add(new TextRun(slice.ToString(), font, width));
        return width;
    }

    private static bool Attaches(Rune rune) =>
        Rune.GetUnicodeCategory(rune)
            is UnicodeCategory.NonSpacingMark
            or UnicodeCategory.EnclosingMark
            or UnicodeCategory.SpacingCombiningMark
            or UnicodeCategory.Format
        || rune.Value is >= 0xFE00 and <= 0xFE0F;

    private static SKFont FontFor(int codePoint, SKFont primary)
    {
        var face = primary.Typeface;
        var key = new CharKey(codePoint, (SKFontStyleWeight)face.FontWeight, face.FontSlant);

        lock (CacheLock)
        {
            if (!Typefaces.TryGetValue(key, out var typeface))
            {
                typeface = Canonical(SKFontManager.Default.MatchCharacter(
                    face.FamilyName, face.FontWeight, face.FontWidth, face.FontSlant, null, codePoint));

                Typefaces[key] = typeface;
            }

            if (typeface is null)
                return primary;

            var fontKey = new FaceSizeKey(KeyOf(typeface), primary.Size);
            if (!FontsBySize.TryGetValue(fontKey, out var font))
            {
                font = new SKFont(typeface, primary.Size)
                {
                    Edging = primary.Edging,
                    Hinting = primary.Hinting,
                    Subpixel = primary.Subpixel
                };

                FontsBySize[fontKey] = font;
            }

            return font;
        }
    }

    private static FaceKey KeyOf(SKTypeface typeface) => new(typeface.FamilyName, typeface.FontWeight, typeface.FontSlant);
    
    private static SKTypeface? Canonical(SKTypeface? match)
    {
        if (match is null)
            return null;

        var key = KeyOf(match);
        if (!Faces.TryGetValue(key, out var canonical))
        {
            Faces[key] = match;
            return match;
        }

        if (!ReferenceEquals(match, canonical) && match.Handle != canonical.Handle)
            match.Dispose();

        return canonical;
    }
}