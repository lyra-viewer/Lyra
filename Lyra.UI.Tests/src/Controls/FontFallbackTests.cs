using Lyra.UI.Components.Primitives;
using Lyra.UI.Theme;
using SkiaSharp;
using Xunit;

namespace Lyra.UI.Tests.Controls;

/// <remarks>
/// The fallback face comes from the host, so tests that need one skip where the
/// system has no font for the character rather than asserting on a guess.
/// </remarks>
public class FontFallbackTests
{
    private const string Cjk = "中文";

    private static SKFont Primary() =>
        new(Fonts.Resolve(Fonts.MonospaceFamily, SKFontStyleWeight.Normal, SKFontStyleSlant.Upright), 14f);

    private static bool HostCanFallBack(SKFont primary, string text)
    {
        if (primary.ContainsGlyph(text[0]) || SKFontManager.Default.MatchCharacter(text[0]) is not { } face)
            return false;

        using var font = new SKFont(face);
        return font.ContainsGlyph(text[0]);
    }

    [Fact]
    public void TextTheFontCoversIsOneRun()
    {
        using var primary = Primary();
        var runs = new List<TextRun>();

        var width = FontFallback.Layout("photo_001.jpg", primary, runs);

        var run = Assert.Single(runs);
        Assert.Same(primary, run.Font);
        Assert.Equal(primary.MeasureText("photo_001.jpg"), width, 0.01);
    }

    [Fact]
    public void AMissingCharacterIsDrawnWithAFaceThatHasIt()
    {
        using var primary = Primary();
        Assert.SkipUnless(HostCanFallBack(primary, Cjk), "No system font covers CJK, or the primary already does.");

        var runs = new List<TextRun>();
        const string text = $"IMG {Cjk}.png";

        var width = FontFallback.Layout(text, primary, runs);

        Assert.Equal(3, runs.Count);
        Assert.Same(primary, runs[0].Font);
        Assert.NotSame(primary, runs[1].Font);
        Assert.Equal(Cjk, runs[1].Text);
        Assert.True(runs[1].Font.ContainsGlyph(Cjk[0]));
        Assert.Same(primary, runs[2].Font);
        Assert.Equal(runs.Sum(r => r.Width), width, 0.01);
    }

    [Fact]
    public void RunsCoverTheWholeTextInOrder()
    {
        using var primary = Primary();
        var runs = new List<TextRun>();
        const string text = "a中b😀c↲d";

        FontFallback.Layout(text, primary, runs);

        Assert.Equal(text, string.Concat(runs.Select(r => r.Text)));
    }

    [Fact]
    public void ACombiningMarkStaysWithItsBaseCharacter()
    {
        using var primary = Primary();
        Assert.SkipUnless(HostCanFallBack(primary, Cjk), "No system font covers CJK, or the primary already does.");

        var runs = new List<TextRun>();

        // A variation selector after a fallback character must not open a run of its own.
        FontFallback.Layout("中️", primary, runs);

        Assert.Single(runs);
    }

    [Fact]
    public void ALabelMeasuresFallbackTextAtTheFallbackWidth()
    {
        using var primary = Primary();
        Assert.SkipUnless(HostCanFallBack(primary, Cjk), "No system font covers CJK, or the primary already does.");

        var viaLabel = Label.MeasureTextWidth(Cjk, Fonts.MonospaceFamily, 14f);
        var viaRuns = FontFallback.Layout(Cjk, primary, null);

        Assert.Equal(viaRuns, viaLabel, 0.01);
    }

    [Fact]
    public void AMarkThePrimaryLacksAfterPrimaryTextGetsAFaceThatHasIt()
    {
        // Devanagari stress sign: a combining mark monospace faces do not carry.
        const string mark = "\u0951";
        using var primary = Primary();
        Assert.SkipUnless(HostCanFallBack(primary, mark), "No system font covers the mark, or the primary already does.");

        var runs = new List<TextRun>();

        FontFallback.Layout("a" + mark, primary, runs);

        var last = runs[^1];
        Assert.Equal(mark, last.Text);
        Assert.NotSame(primary, last.Font);
        Assert.True(last.Font.ContainsGlyph(mark[0]));
    }
}