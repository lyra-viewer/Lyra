using Lyra.UI.Components;
using Lyra.UI.Components.Controls;
using Lyra.UI.Components.Controls.TreeView;
using Lyra.UI.Components.Layout;
using Lyra.UI.SupportingTypes;
using SkiaSharp;
using Xunit;

namespace Lyra.UI.Tests.Controls;

public class ChildrenViewTests
{
    private static IComponent Row() => new HStack { HorizontalSize = SizeMode.Expand };

    [Fact]
    public void ListViewReturnsTheSameChildrenInstanceAcrossReads()
    {
        var list = new ListView<int>([0, 1, 2], (_, _) => Row());
        list.Measure(new SKSize(100, 100));

        Assert.Same(list.Children, list.Children);
    }

    [Fact]
    public void TreeViewReturnsTheSameChildrenInstanceAcrossReads()
    {
        var tree = new TreeView<int>([new TreeNode<int>(0), new TreeNode<int>(1)], (_, _) => Row());
        tree.Measure(new SKSize(100, 100));

        Assert.Same(tree.Children, tree.Children);
    }

    // --------------------------------------------------------
    //  The view still tracks the data
    // --------------------------------------------------------

    [Fact]
    public void ListViewChildrenTrackTheRowsAfterUpdateData()
    {
        var list = new ListView<int>([0, 1, 2], (_, _) => Row());
        list.Measure(new SKSize(100, 100));
        Assert.Equal(3, list.Children.Count);

        list.UpdateData([0]);
        list.Measure(new SKSize(100, 100));

        Assert.Single(list.Children);
    }

    [Fact]
    public void TreeViewChildrenTrackTheRowsAfterUpdateData()
    {
        var tree = new TreeView<int>([new TreeNode<int>(0), new TreeNode<int>(1)], (_, _) => Row());
        tree.Measure(new SKSize(100, 100));
        Assert.Equal(2, tree.Children.Count);

        tree.UpdateData([new TreeNode<int>(0)]);
        tree.Measure(new SKSize(100, 100));

        Assert.Single(tree.Children);
    }

    [Fact]
    public void ListViewChildrenAreEmptyAfterDispose()
    {
        var list = new ListView<int>([0, 1], (_, _) => Row());
        list.Measure(new SKSize(100, 100));

        list.Dispose();

        Assert.Empty(list.Children);
    }
}
