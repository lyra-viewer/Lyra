using Lyra.UI.Components;
using Lyra.UI.Components.Controls.TreeView;
using Lyra.UI.Components.Layout;
using Lyra.UI.SupportingTypes;
using SkiaSharp;
using Xunit;

namespace Lyra.UI.Tests.Controls;

/// <summary>A row the tree may not pick toggles when it has children, and otherwise ignores the click.</summary>
public class TreeViewPickTests
{
    private const float Width = 300f;
    private const float RowHeight = 30f;

    // Well right of the arrow column, so only CanPick decides what a click does.
    private const float TextX = 150f;

    private static float RowY(int row) => row * RowHeight + RowHeight / 2f;

    private readonly record struct Fixture(TreeView<string> Tree, TreeNode<string> Group);

    /// <summary>Rows: group, open (0), "blocked" (1), "free" (2).</summary>
    private static Fixture Tree()
    {
        var group = new TreeNode<string>("group") { IsExpanded = true };
        group.AddChildren(["blocked", "free"]);

        var tree = new TreeView<string>([group], (_, _) => new HStack
        {
            HorizontalSize = SizeMode.Expand,
            VerticalSize = SizeMode.Fixed,
            Height = RowHeight
        })
        {
            HorizontalSize = SizeMode.Expand,
            VerticalSize = SizeMode.Expand,
            CanPick = node => node.IsLeaf && node.Data != "blocked"
        };

        return new Fixture(tree, group);
    }

    private static void Frame(IComponent root)
    {
        using var surface = SKSurface.Create(new SKImageInfo((int)Width, 200));

        root.Measure(new SKSize(Width, 200));
        root.Resolve();
        root.Arrange(new SKRect(0, 0, Width, 200));
        root.Render(surface.Canvas);
    }

    private static void Click(TreeView<string> tree, int row)
    {
        tree.OnPointerDown(new SKPoint(TextX, RowY(row)));
        tree.OnPointerUp(new SKPoint(TextX, RowY(row)));
    }

    [Fact]
    public void AGroupThatCannotBePickedTogglesInstead()
    {
        var (tree, group) = Tree();
        var picks = new List<string>();
        tree.Picked += node => picks.Add(node.Data);
        Frame(tree);

        Click(tree, 0);

        Assert.False(group.IsExpanded);
        Assert.Empty(picks);
        Assert.Null(tree.PickedNode);
    }

    [Fact]
    public void ALeafThatCannotBePickedIgnoresTheClick()
    {
        var (tree, _) = Tree();
        var picks = new List<string>();
        tree.Picked += node => picks.Add(node.Data);
        Frame(tree);

        Click(tree, 1);
        Assert.Empty(picks);
        Assert.Null(tree.PickedNode);

        Click(tree, 2);
        Assert.Equal(["free"], picks);
        Assert.Equal("free", tree.PickedNode?.Data);
    }
}