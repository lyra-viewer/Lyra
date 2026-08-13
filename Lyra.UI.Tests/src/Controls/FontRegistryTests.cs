using Lyra.UI.Components.Primitives;
using Lyra.UI.Theme;
using SkiaSharp;
using Xunit;

namespace Lyra.UI.Tests.Controls;

/// <summary>
/// A font shipped inside the application is invisible to the OS font manager, so
/// SKTypeface.FromFamilyName cannot find it - and does not say so, it substitutes
/// something else and returns successfully. Bundling a font therefore only works
/// if resolution consults a registry first, and the failure mode of getting that
/// wrong is a silently wrong typeface rather than an error.
///
/// These run against a synthetic registration rather than a real font file, so
/// they test the resolution path itself and stay valid whatever the host ships.
/// </summary>
[Collection(nameof(FontRegistryTests))]
[CollectionDefinition(nameof(FontRegistryTests), DisableParallelization = true)]
public class FontRegistryTests
{
    // A family name no system could plausibly have, so a hit can only come from
    // the registry.
    private const string Fictional = "Lyra Test Face ZZ99";

    private static SKTypeface Distinctive()
    {
        var typeface = SKTypeface.FromFamilyName(Fonts.MonospaceFamily, SKFontStyle.Normal);
        Assert.NotNull(typeface);
        return typeface;
    }

    [Fact]
    public void ARegisteredTypefaceIsReturnedForItsFamily()
    {
        var registered = Distinctive();
        Fonts.Register(Fictional, SKFontStyleWeight.Normal, SKFontStyleSlant.Upright, registered);

        var resolved = Fonts.Resolve(Fictional, SKFontStyleWeight.Normal, SKFontStyleSlant.Upright);

        Assert.Same(registered, resolved);
    }

    [Fact]
    public void AnUnregisteredStyleFallsBackToThePlatform()
    {
        var registered = Distinctive();
        Fonts.Register(Fictional, SKFontStyleWeight.Normal, SKFontStyleSlant.Upright, registered);

        // Bold was never registered: the platform answers instead of this failing.
        var resolved = Fonts.Resolve(Fictional, SKFontStyleWeight.Bold, SKFontStyleSlant.Upright);

        Assert.NotNull(resolved);
        Assert.NotSame(registered, resolved);
    }

    [Fact]
    public void AnUnknownFamilyStillResolvesToSomething()
    {
        // Skia substitutes rather than returning null, so callers never have to
        // handle a missing typeface.
        var resolved = Fonts.Resolve("No Such Family QQ7", SKFontStyleWeight.Normal, SKFontStyleSlant.Upright);

        Assert.NotNull(resolved);
    }

    [Fact]
    public void RegistrationRejectsNonsense()
    {
        var typeface = Distinctive();

        Assert.Throws<ArgumentNullException>(() =>
            Fonts.Register(Fictional, SKFontStyleWeight.Normal, SKFontStyleSlant.Upright, null!));

        Assert.Throws<ArgumentException>(() =>
            Fonts.Register(" ", SKFontStyleWeight.Normal, SKFontStyleSlant.Upright, typeface));
    }

    [Fact]
    public void ALabelRendersWithTheRegisteredFace()
    {
        // The end-to-end claim: registering a face changes what a Label draws
        // with, not merely what Fonts.Resolve returns.
        var registered = Distinctive();
        Fonts.Register(Fictional, SKFontStyleWeight.Normal, SKFontStyleSlant.Upright, registered);

        var viaRegistry = new Label("measure me") { FontFamily = Fictional };
        var viaPlatform = new Label("measure me") { FontFamily = Fonts.MonospaceFamily };

        var registeredWidth = viaRegistry.Measure(new SKSize(1000, 100)).Width;
        var platformWidth = viaPlatform.Measure(new SKSize(1000, 100)).Width;

        // Same underlying face, so identical metrics - which only holds if the
        // fictional family resolved through the registry rather than falling
        // back to an arbitrary substitute.
        Assert.Equal(platformWidth, registeredWidth, 3);
        Assert.True(registeredWidth > 0);
    }

    [Fact]
    public void TheDefaultMonospaceFamilyCanBeNominated()
    {
        var original = Fonts.MonospaceFamily;

        try
        {
            Fonts.SetDefaultMonospace(Fictional);
            Assert.Equal(Fictional, Fonts.MonospaceFamily);

            // A label built after the change picks it up.
            Assert.Equal(Fictional, new Label("x").FontFamily);
        }
        finally
        {
            Fonts.SetDefaultMonospace(original);
        }
    }
}