using Xunit;

namespace Lyra.Core.Tests.Startup;

/// <summary>
/// Which shared library gets loaded when a machine carries more than one version of it. The
/// listing these read from is a real library directory, so it holds symlinks, real files and
/// whatever else shares the name.
/// </summary>
public class SonameVersionTests
{
    private const string Heif = "libheif.so";

    private static string? Pick(params string[] candidates) => SonameVersion.SelectHighest(candidates, Heif);

    [Fact]
    public void TenBeatsTwo_WhichSortingAsTextWouldNot()
    {
        // The whole point: ordered as strings, "libheif.so.2" comes out above "libheif.so.10",
        // and a machine mid-ABI-transition would be handed the older library.
        Assert.Equal("/usr/lib/libheif.so.10", Pick("/usr/lib/libheif.so.2", "/usr/lib/libheif.so.10"));
        Assert.Equal("/usr/lib/libheif.so.10", Pick("/usr/lib/libheif.so.10", "/usr/lib/libheif.so.2"));
    }

    [Fact]
    public void LaterComponentsDecideWhenTheMajorsMatch()
    {
        Assert.Equal("/usr/lib/libheif.so.1.19.8", Pick(
            "/usr/lib/libheif.so.1.19.8",
            "/usr/lib/libheif.so.1.9.20",
            "/usr/lib/libheif.so.1.19.7"));
    }

    [Fact]
    public void AMajorOnTopOfAFullerVersionOfALowerOneStillWins()
    {
        Assert.Equal("/usr/lib/libheif.so.2", Pick("/usr/lib/libheif.so.1.19.8", "/usr/lib/libheif.so.2"));
    }

    [Fact]
    public void TheSonameSymlinkAndTheRealFileBothCount()
    {
        // Both name the same library; the fuller version is the real file behind the symlink.
        Assert.Equal("/usr/lib/libheif.so.1.19.8", Pick("/usr/lib/libheif.so.1", "/usr/lib/libheif.so.1.19.8"));
    }

    [Fact]
    public void NonNumericSuffixesAreNotLibraries()
    {
        // A directory listing turns up more than libraries; loading one of these in place of a
        // library fails a long way from here.
        Assert.Null(Pick("/usr/lib/libheif.so.1.19.8-gdb.py"));
        Assert.Null(Pick("/usr/lib/libheif.so.debug"));
        Assert.Null(Pick("/usr/lib/libheif.so.1.2.3.debug"));

        Assert.Equal("/usr/lib/libheif.so.1", Pick("/usr/lib/libheif.so.debug", "/usr/lib/libheif.so.1"));
    }

    [Fact]
    public void ANameThatOnlyLooksLikeTheLibraryIsRefused()
    {
        Assert.Null(Pick("/usr/lib/libheifsomething.so.1"));
        Assert.Null(Pick("/usr/lib/libheif.so"));       // the unversioned form has already been tried
        Assert.Null(Pick("/usr/lib/LIBHEIF.SO.1"));     // Linux file names are case-sensitive
        Assert.Null(Pick("/usr/lib/libheif.so."));
    }

    [Fact]
    public void NegativeAndPaddedComponentsAreRefusedRatherThanReinterpreted()
    {
        Assert.Null(Pick("/usr/lib/libheif.so.-1"));
        Assert.Null(Pick("/usr/lib/libheif.so. 1"));
    }

    [Fact]
    public void NothingToChooseFromYieldsNothing()
    {
        Assert.Null(Pick());
    }

    [Fact]
    public void RealisticListing_PicksTheNewestLibrary()
    {
        // What /usr/lib/x86_64-linux-gnu actually looks like part-way through a transition.
        Assert.Equal("/usr/lib/libheif.so.2.1.0", Pick(
            "/usr/lib/libheif.so.1",
            "/usr/lib/libheif.so.1.19.8",
            "/usr/lib/libheif.so.2",
            "/usr/lib/libheif.so.2.1.0",
            "/usr/lib/libheif.so.1.19.8-gdb.py"));
    }
}
