using Lyra.UI.Components;
using Lyra.UI.Components.Controls;
using Lyra.UI.Components.Controls.TreeView;
using Lyra.UI.Components.Layout;
using Lyra.UI.SupportingTypes;
using SkiaSharp;
using Xunit;

namespace Lyra.UI.Tests.Controls;

/// <summary>
/// ListView and TreeView used to hold the caller's list by reference, so editing that list
/// after handing it over changed the data without marking the rows dirty. The control kept
/// rendering the old rows against the new data, and the pick index pointed at whatever now
/// sat at that position. Both now copy on entry; UpdateData is the only way to publish a
/// change.
/// </summary>
public class DataOwnershipTests
{
    private static IComponent Row() => new HStack
    {
        HorizontalSize = SizeMode.Expand,
        VerticalSize = SizeMode.Fixed,
        Height = 10
    };

    private static readonly SKSize Available = new(200, 200);

    // --------------------------------------------------------
    //  ListView
    // --------------------------------------------------------

    [Fact]
    public void EditingTheSourceListAfterConstructionDoesNotChangeTheRows()
    {
        var source = new List<int> { 0, 1, 2 };
        var list = new ListView<int>(source, (_, _) => Row());
        list.Measure(Available);

        source.Add(3);
        source.Add(4);
        list.Measure(Available);

        Assert.Equal(3, list.Children.Count);
    }

    [Fact]
    public void EditingTheSourceListAfterUpdateDataDoesNotChangeTheRows()
    {
        var list = new ListView<int>([], (_, _) => Row());

        var source = new List<int> { 0, 1 };
        list.UpdateData(source);
        list.Measure(Available);

        source.Clear();
        list.Measure(Available);

        Assert.Equal(2, list.Children.Count);
    }

    [Fact]
    public void RemoveDoesNotMutateTheCallersList()
    {
        var source = new List<int> { 0, 1, 2 };
        var list = new ListView<int>(source, (_, _) => Row());

        Assert.True(list.Remove(i => i == 1));

        Assert.Equal([0, 1, 2], source);

        list.Measure(Available);
        Assert.Equal(2, list.Children.Count);
    }

    [Fact]
    public void ListViewRejectsNullArguments()
    {
        Assert.Throws<ArgumentNullException>(() => new ListView<int>(null!, (_, _) => Row()));
        Assert.Throws<ArgumentNullException>(() => new ListView<int>([], null!));
        Assert.Throws<ArgumentNullException>(() => new ListView<int>([], (_, _) => Row()).UpdateData(null!));
    }

    // --------------------------------------------------------
    //  TreeView
    // --------------------------------------------------------

    [Fact]
    public void EditingTheSourceRootListDoesNotChangeTheRows()
    {
        var roots = new List<TreeNode<int>> { new(0), new(1) };
        var tree = new TreeView<int>(roots, (_, _) => Row());
        tree.Measure(Available);

        roots.Add(new TreeNode<int>(2));
        tree.Measure(Available);

        Assert.Equal(2, tree.Children.Count);
    }

    [Fact]
    public void NodeObjectsStayShared()
    {
        // Deliberate: TreeNode is the data model and carries expand state, so the
        // caller and the view must see the same nodes.
        var node = new TreeNode<int>(0);
        node.AddChild(1);

        var tree = new TreeView<int>([node], (_, _) => Row());
        node.IsExpanded = true;
        tree.InvalidateRows();
        tree.Measure(Available);

        Assert.Equal(2, tree.Children.Count);
    }

    [Fact]
    public void TreeViewRejectsNullArguments()
    {
        Assert.Throws<ArgumentNullException>(() => new TreeView<int>(null!, (_, _) => Row()));
        Assert.Throws<ArgumentNullException>(() => new TreeView<int>([], null!));
        Assert.Throws<ArgumentNullException>(() => new TreeView<int>([], (_, _) => Row()).UpdateData(null!));
    }
}
