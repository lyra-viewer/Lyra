using Lyra.Imaging.Loading;
using Xunit;

namespace Lyra.Imaging.Tests.Loading;

/// <summary>
/// Where distances are measured from when the budget picks what to evict.
/// </summary>
public class EvictionCentreTests
{
    private static readonly string[] Window = ["a.exr", "b.exr", "c.exr", "d.exr", "e.exr"];

    [Fact]
    public void TheCurrentImageIsTheOrigin()
    {
        Assert.Equal(0, ImageLoader.Centre(Window, "a.exr"));
        Assert.Equal(2, ImageLoader.Centre(Window, "c.exr"));
        Assert.Equal(4, ImageLoader.Centre(Window, "e.exr"));
    }

    [Fact]
    public void NoCurrentImageFallsBackToTheMiddle()
    {
        Assert.Equal(2, ImageLoader.Centre(Window, null));
    }
    
    [Fact]
    public void AnUnknownCurrentImageFallsBackToTheMiddleRatherThanTheEdge()
    {
        var centre = ImageLoader.Centre(Window, "not-in-the-window.exr");

        Assert.Equal(2, centre);
        Assert.NotEqual(0, centre);
    }

    [Fact]
    public void AnEmptyWindowHasNothingToCentreOn()
    {
        Assert.Equal(0, ImageLoader.Centre([], "a.exr"));
        Assert.Equal(0, ImageLoader.Centre([], null));
    }
}
