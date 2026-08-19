using Lyra.Renderer;
using Lyra.UI.Theme;
using SkiaSharp;
using Xunit;

namespace Lyra.Core.Tests.Rendering;

public class BundledFontsTests
{
    [Fact]
    public void TheLicenceTextShipsWithTheFont()
    {
        using var stream = typeof(BundledFonts).Assembly.GetManifestResourceStream("LyraViewer.Fonts.OFL.txt");

        Assert.NotNull(stream);

        using var reader = new StreamReader(stream);
        var text = reader.ReadToEnd();

        Assert.Contains("SIL OPEN FONT LICENSE", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("JetBrains Mono Project Authors", text);
    }

    [Theory]
    [InlineData("LyraViewer.Fonts.JetBrainsMono-Regular.ttf")]
    [InlineData("LyraViewer.Fonts.JetBrainsMono-Bold.ttf")]
    public void EachFaceIsEmbeddedAndParses(string resourceName)
    {
        using var stream = typeof(BundledFonts).Assembly.GetManifestResourceStream(resourceName);
        Assert.NotNull(stream);

        using var typeface = SKTypeface.FromStream(stream);

        Assert.NotNull(typeface);
        Assert.Equal(BundledFonts.MonospaceFamily, typeface.FamilyName);
    }

    [Fact]
    public void RegisteringMakesTheFamilyResolvableByName()
    {
        BundledFonts.Register();

        var resolved = Fonts.Resolve(BundledFonts.MonospaceFamily, SKFontStyleWeight.Normal, SKFontStyleSlant.Upright);

        // The point of the registry: this family is not installed on the machine,
        // so a platform lookup would have substituted something else.
        Assert.Equal(BundledFonts.MonospaceFamily, resolved.FamilyName);
    }

    [Fact]
    public void BoldResolvesToTheBundledBoldFace()
    {
        BundledFonts.Register();

        var regular = Fonts.Resolve(BundledFonts.MonospaceFamily, SKFontStyleWeight.Normal, SKFontStyleSlant.Upright);
        var bold = Fonts.Resolve(BundledFonts.MonospaceFamily, SKFontStyleWeight.Bold, SKFontStyleSlant.Upright);

        Assert.Equal(BundledFonts.MonospaceFamily, bold.FamilyName);
        Assert.NotSame(regular, bold);
        Assert.True(bold.FontWeight > regular.FontWeight, $"expected the bold face to be heavier, got {bold.FontWeight} vs {regular.FontWeight}");
    }

    [Fact]
    public void RegisteringIsIdempotent()
    {
        BundledFonts.Register();
        var first = Fonts.Resolve(BundledFonts.MonospaceFamily, SKFontStyleWeight.Normal, SKFontStyleSlant.Upright);

        BundledFonts.Register();
        var second = Fonts.Resolve(BundledFonts.MonospaceFamily, SKFontStyleWeight.Normal, SKFontStyleSlant.Upright);

        // Re-registering must not build a second typeface for the same face -
        // these are never disposed, so duplicates would be a leak.
        Assert.Same(first, second);
    }

    [Fact]
    public void TheBundledFamilyBecomesTheDefault()
    {
        BundledFonts.Register();

        Assert.Equal(BundledFonts.MonospaceFamily, Fonts.MonospaceFamily);
    }
}