using Lyra.UI.Components.Controls.TreeView;
using Xunit;

namespace Lyra.UI.Tests.Layout;

public class PathTreeBuilderTests
{
    [Fact]
    public void ChildrenHangUnderTheirOwnParent()
    {
        var roots = PathTreeBuilder.Build(["photos", "photos/vacation", "photos/work", "docs"]);

        Assert.Equal(2, roots.Count);
        Assert.Equal("photos", roots[0].Data);
        Assert.Equal(["photos/vacation", "photos/work"], roots[0].Children.Select(c => c.Data));
        Assert.Equal("docs", roots[1].Data);
        Assert.Empty(roots[1].Children);
    }

    [Fact]
    public void ASiblingSortingBetweenADirectoryAndItsContentsDoesNotAdoptThem()
    {
        var roots = PathTreeBuilder.Build(["exampletiffs", "tiff", "tiff-large", "tiff/img"]);

        Assert.Equal(["exampletiffs", "tiff", "tiff-large"], roots.Select(r => r.Data));

        var tiff = roots[1];
        Assert.Equal(["tiff/img"], tiff.Children.Select(c => c.Data));

        Assert.Empty(roots[0].Children);
        Assert.Empty(roots[2].Children);
    }

    [Theory]
    [InlineData("shots-2024")]
    [InlineData("shots.old")]
    [InlineData("shots 2")]
    [InlineData("shots!")]
    public void TheSameHoldsForEveryCharacterThatSortsBeforeTheSeparator(string sibling)
    {
        var paths = new List<string> { "shots", sibling, "shots/raw" };
        paths.Sort(StringComparer.Ordinal);

        var roots = PathTreeBuilder.Build(paths);

        var shots = Assert.Single(roots, r => r.Data == "shots");
        Assert.Equal(["shots/raw"], shots.Children.Select(c => c.Data));
        Assert.Empty(Assert.Single(roots, r => r.Data == sibling).Children);
    }

    [Fact]
    public void NestingIsRecordedAsDepth()
    {
        var roots = PathTreeBuilder.Build(["a", "a/b", "a/b/c"]);

        var a = roots[0];
        var b = a.Children[0];
        var c = b.Children[0];

        Assert.Equal(0, a.Depth);
        Assert.Equal(1, b.Depth);
        Assert.Equal(2, c.Depth);
        Assert.Same(b, c.Parent);
    }

    [Fact]
    public void APathWithNoParentInTheListIsARoot()
    {
        var roots = PathTreeBuilder.Build(["other", "missing/deep/leaf"]);

        Assert.Equal(["other", "missing/deep/leaf"], roots.Select(r => r.Data));
    }

    [Fact]
    public void SurroundingSlashesAndEmptyPathsAreIgnored()
    {
        var roots = PathTreeBuilder.Build(["/photos/", "", "/photos/vacation/"]);

        var photos = Assert.Single(roots);
        Assert.Equal("/photos/", photos.Data);
        Assert.Equal(["/photos/vacation/"], photos.Children.Select(c => c.Data));
    }
}