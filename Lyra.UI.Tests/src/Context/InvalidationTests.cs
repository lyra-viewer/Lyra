using Lyra.UI;
using Lyra.UI.Components;
using Lyra.UI.Components.Controls;
using Lyra.UI.Components.Controls.TreeView;
using Lyra.UI.Components.Layout;
using Lyra.UI.Components.Primitives;
using Lyra.UI.SupportingTypes;
using SkiaSharp;
using Xunit;
using Button = Lyra.UI.Components.Controls.Button.Button;

namespace Lyra.UI.Tests.Context;

public class InvalidationTests
{
    private static Label Attached(UIContext context, out VStack root)
    {
        var label = new Label("initial");
        root = new VStack();
        root.AddComponent(label);
        context.Root = root;
        context.ClearDirty();
        return label;
    }

    // --------------------------------------------------------
    //  Context reaches the tree
    // --------------------------------------------------------

    [Fact]
    public void AssigningALayerRootStampsTheWholeSubtree()
    {
        using var context = new UIContext();

        var leaf = new Label("x");
        var middle = new VStack();
        var root = new VStack();

        // Built entirely detached, the way every section builds its tree.
        middle.AddComponent(leaf);
        root.AddComponent(middle);

        Assert.Null(leaf.Context);

        context.Root = root;

        Assert.Same(context, root.Context);
        Assert.Same(context, middle.Context);
        Assert.Same(context, leaf.Context);
    }

    [Fact]
    public void AddingAChildToAnAttachedParentStampsIt()
    {
        using var context = new UIContext();
        var root = new VStack();
        context.Root = root;

        var late = new Label("late");
        Assert.Null(late.Context);

        root.AddComponent(late);

        Assert.Same(context, late.Context);
    }

    [Fact]
    public void ReplacingALayerRootReleasesTheOldTree()
    {
        using var context = new UIContext();

        var first = new VStack();
        var orphan = new Label("orphan");
        first.AddComponent(orphan);
        context.Root = first;
        Assert.Same(context, orphan.Context);

        context.Root = new VStack();

        // A detached tree must not be able to dirty a context it left.
        Assert.Null(orphan.Context);

        context.ClearDirty();
        orphan.Text = "changed";
        Assert.False(context.IsDirty);
    }

    [Fact]
    public void MutatingADetachedComponentIsHarmless()
    {
        var label = new Label("x") { Present = false, Width = 20 };
        label.Text = "y";

        Assert.Equal("y", label.Text);
    }

    // --------------------------------------------------------
    //  Composite controls reach their private children
    // --------------------------------------------------------

    [Fact]
    public void ACollapsibleHeaderCanInvalidate()
    {
        using var context = new UIContext();
        var collapsible = new Collapsible("TITLE");
        context.Root = collapsible;
        context.ClearDirty();

        collapsible.Title = "CHANGED";

        Assert.True(context.IsDirty);
    }

    [Fact]
    public void AButtonLabelCanInvalidate()
    {
        using var context = new UIContext();
        var button = new Button("go");
        context.Root = button;
        context.ClearDirty();

        button.Text = "stop";

        Assert.True(context.IsDirty);
    }

    [Fact]
    public void ListRowsAreStampedAsTheyAreBuilt()
    {
        using var context = new UIContext();
        var list = new ListView<int>([0, 1], (_, _) => new HStack());
        context.Root = list;

        list.Measure(new SKSize(100, 100));

        Assert.All(list.Children, child => Assert.Same(context, child.Context));
    }

    // --------------------------------------------------------
    //  Setters invalidate on change
    // --------------------------------------------------------

    [Fact]
    public void ChangingTextInvalidates()
    {
        using var context = new UIContext();
        var label = Attached(context, out _);

        label.Text = "changed";

        Assert.True(context.IsDirty);
    }

    [Theory]
    [InlineData("present")]
    [InlineData("visible")]
    [InlineData("enabled")]
    [InlineData("width")]
    [InlineData("padding")]
    [InlineData("align")]
    [InlineData("background")]
    [InlineData("sizemode")]
    public void ChangingALayoutPropertyInvalidates(string property)
    {
        using var context = new UIContext();
        var label = Attached(context, out _);

        switch (property)
        {
            case "present": label.Present = false; break;
            case "visible": label.Visible = false; break;
            case "enabled": label.Enabled = false; break;
            case "width": label.Width = 123f; break;
            case "padding": label.Padding = new Padding(4); break;
            case "align": label.HorizontalAlign = HAlign.Right; break;
            case "background": label.BackgroundColor = SKColors.Red; break;
            case "sizemode": label.HorizontalSize = SizeMode.Expand; break;
        }

        Assert.True(context.IsDirty);
    }

    [Fact]
    public void ReplacingListDataInvalidates()
    {
        using var context = new UIContext();
        var list = new ListView<int>([0], (_, _) => new HStack());
        context.Root = list;
        context.ClearDirty();

        list.UpdateData([1, 2]);

        Assert.True(context.IsDirty);
    }

    [Fact]
    public void ReplacingTreeDataInvalidates()
    {
        using var context = new UIContext();
        var tree = new TreeView<int>([new TreeNode<int>(0)], (_, _) => new HStack());
        context.Root = tree;
        context.ClearDirty();

        tree.UpdateData([new TreeNode<int>(1)]);

        Assert.True(context.IsDirty);
    }

    [Fact]
    public void TogglingACollapsibleInvalidates()
    {
        using var context = new UIContext();
        var collapsible = new Collapsible("T");
        context.Root = collapsible;
        context.ClearDirty();

        collapsible.IsExpanded = true;

        Assert.True(context.IsDirty);
    }

    // --------------------------------------------------------
    //  The equality guard
    // --------------------------------------------------------

    [Theory]
    [InlineData("text")]
    [InlineData("present")]
    [InlineData("width")]
    [InlineData("padding")]
    [InlineData("background")]
    public void WritingTheSameValueBackDoesNotInvalidate(string property)
    {
        using var context = new UIContext();
        var label = Attached(context, out _);

        // Establish the value, then write it again - the shape of a Refresh that
        // runs every frame and mostly changes nothing.
        switch (property)
        {
            case "text":
                label.Text = "same";
                context.ClearDirty();
                label.Text = "same";
                break;
            case "present":
                label.Present = false;
                context.ClearDirty();
                label.Present = false;
                break;
            case "width":
                label.Width = 40f;
                context.ClearDirty();
                label.Width = 40f;
                break;
            case "padding":
                label.Padding = new Padding(3);
                context.ClearDirty();
                label.Padding = new Padding(3);
                break;
            case "background":
                label.BackgroundColor = SKColors.Red;
                context.ClearDirty();
                label.BackgroundColor = SKColors.Red;
                break;
        }

        Assert.False(context.IsDirty);
    }

    [Fact]
    public void ATogglePutBackWhereItWasDoesNotInvalidate()
    {
        using var context = new UIContext();
        var collapsible = new Collapsible("T") { IsExpanded = true };
        context.Root = collapsible;
        context.ClearDirty();

        collapsible.IsExpanded = true;

        Assert.False(context.IsDirty);
    }
}