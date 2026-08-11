using Lyra.UI.Components.Layout;
using Lyra.UI.SupportingTypes;
using SkiaSharp;
using Xunit;

namespace Lyra.UI.Tests.Layout;

/// <summary>
/// StackBase's space distribution is the most intricate code in the layout pass and had no
/// tests. The behavior is also easy to assume wrongly, so these are written from measured
/// behavior rather than from reading the code.
///
/// The rule that catches people out: Flexible does NOT fill its parent. It measures like
/// Shrink and accepts being squeezed when siblings need the room - the cap is always its own
/// content. Expand is the mode that fills.
/// </summary>
public class StackFlexDistributionTests
{
    private const float Available = 300f;

    private static VStack Fixed(float width) => new()
    {
        HorizontalSize = SizeMode.Fixed,
        VerticalSize = SizeMode.Fixed,
        Width = width,
        Height = 10
    };

    /// <summary>A Flexible box whose intrinsic content is exactly <paramref name="contentWidth"/>.</summary>
    private static HStack Flexible(float contentWidth, float? maxWidth = null)
    {
        var flexible = new HStack { HorizontalSize = SizeMode.Flexible, MaxWidth = maxWidth };
        flexible.AddComponent(Fixed(contentWidth));
        return flexible;
    }

    private static HStack Row(params Lyra.UI.Components.IComponent[] children)
    {
        var row = new HStack { HorizontalSize = SizeMode.Expand };
        row.AddComponents(children);
        return row;
    }

    private static void Layout(HStack row, float available = Available)
    {
        row.Measure(new SKSize(available, 100));
        row.Resolve();
        row.Arrange(new SKRect(0, 0, available, 100));
    }

    // --------------------------------------------------------
    //  Flexible is capped by its own content
    // --------------------------------------------------------

    [Fact]
    public void FlexibleDoesNotGrowIntoSpareRoom()
    {
        var flexible = Flexible(100);
        var row = Row(Fixed(50), flexible);

        Layout(row);

        // 150 spare, and it takes none of it.
        Assert.Equal(100f, flexible.ArrangedBounds.Width, 3);
    }

    [Fact]
    public void ExpandDoesGrowIntoSpareRoom()
    {
        // The contrast that makes the previous test meaningful.
        var expanding = new HStack { HorizontalSize = SizeMode.Expand };
        expanding.AddComponent(Fixed(100));
        var row = Row(Fixed(50), expanding);

        Layout(row);

        Assert.Equal(250f, expanding.ArrangedBounds.Width, 3);
    }

    // --------------------------------------------------------
    //  Flexible yields when siblings need the room
    // --------------------------------------------------------

    [Fact]
    public void FlexibleIsSqueezedIntoWhateverTheBudgetLeaves()
    {
        var flexible = Flexible(280);
        var row = Row(Fixed(50), flexible);

        Layout(row);

        // 300 total less the fixed 50.
        Assert.Equal(250f, flexible.ArrangedBounds.Width, 3);
    }

    [Fact]
    public void CompetingFlexiblesReleaseWhatTheyDoNotNeed()
    {
        // Fair share is 150 each, but the smaller one only wants 100, so the
        // larger one collects the 50 it gives back instead of both being cut.
        var greedy = Flexible(250);
        var modest = Flexible(100);
        var row = Row(greedy, modest);

        Layout(row);

        Assert.Equal(200f, greedy.ArrangedBounds.Width, 3);
        Assert.Equal(100f, modest.ArrangedBounds.Width, 3);
    }

    [Fact]
    public void EquallyGreedyFlexiblesSplitTheBudget()
    {
        var left = Flexible(250);
        var right = Flexible(250);
        var row = Row(left, right);

        Layout(row);

        Assert.Equal(150f, left.ArrangedBounds.Width, 3);
        Assert.Equal(150f, right.ArrangedBounds.Width, 3);
    }

    [Fact]
    public void MaxWidthCapsAFlexibleBelowItsContent()
    {
        var flexible = Flexible(200, maxWidth: 120);
        var row = Row(Fixed(50), flexible);

        Layout(row);

        Assert.Equal(120f, flexible.ArrangedBounds.Width, 3);
    }

    // --------------------------------------------------------
    //  What Resolve is for
    // --------------------------------------------------------

    [Fact]
    public void ResolveGivesBackSpaceToAFlexibleThatWasSqueezedAtMeasureTime()
    {
        // The one case where the Resolve pass changes an answer: the child is cut
        // to the measure-time budget of 250, then the row's own MinWidth makes it
        // bigger than the space it measured against, so the surplus is handed
        // back - up to the child's content, never beyond.
        var flexible = Flexible(280);

        var row = new HStack { HorizontalSize = SizeMode.Expand, MinWidth = 400 };
        row.AddComponents(Fixed(50), flexible);

        row.Measure(new SKSize(Available, 100));
        Assert.Equal(250f, flexible.DesiredSize.Width, 3);

        row.Resolve();

        Assert.Equal(280f, flexible.DesiredSize.Width, 3);
    }

    // --------------------------------------------------------
    //  The deficit path
    // --------------------------------------------------------
    //  A Flexible child with a MinWidth cannot be squeezed below it, so the
    //  measured children can end up wanting more than the row has. The other
    //  Flexible - the one with no floor of its own - gives up the difference.
    //  Redistribution runs in rounds: a child that cannot give its full share
    //  drops out and the rest is re-shared among those that can.
    // --------------------------------------------------------

    [Fact]
    public void AFlexibleAbsorbsTheOverflowCausedBySiblingMinimums()
    {
        var pinned = Flexible(10);
        pinned.MinWidth = 200;

        var free = Flexible(180);

        // Measured: fixed 10 + pinned 200 + free 145 = 355 in a 300 row.
        var row = Row(Fixed(10), pinned, free);
        Layout(row);

        Assert.Equal(200f, pinned.ArrangedBounds.Width, 3);
        Assert.Equal(90f, free.ArrangedBounds.Width, 3);
    }

    [Fact]
    public void ShrinkingStopsAtTheDefaultFloor()
    {
        // A larger minimum means a larger deficit, but the giving child is never
        // cut below 60 - the row overflows rather than collapsing a child to
        // nothing.
        var pinned = Flexible(10);
        pinned.MinWidth = 260;

        var free = Flexible(180);

        var row = Row(Fixed(10), pinned, free);
        Layout(row);

        Assert.Equal(260f, pinned.ArrangedBounds.Width, 3);
        Assert.Equal(60f, free.ArrangedBounds.Width, 3);
    }

    // --------------------------------------------------------
    //  Spacing takes part in the budget
    // --------------------------------------------------------

    [Fact]
    public void SpacingComesOutOfTheFlexibleBudget()
    {
        var flexible = Flexible(280);
        var row = new HStack { HorizontalSize = SizeMode.Expand, Spacing = 20 };
        row.AddComponents(Fixed(50), flexible);

        Layout(row);

        // 300 less the fixed 50 less the single 20 gap.
        Assert.Equal(230f, flexible.ArrangedBounds.Width, 3);
    }

    [Fact]
    public void NonPresentChildrenTakeNoBudgetAndNoSpacing()
    {
        var hidden = Fixed(100);
        var flexible = Flexible(280);
        var row = new HStack { HorizontalSize = SizeMode.Expand, Spacing = 20 };
        row.AddComponents(Fixed(50), hidden, flexible);

        hidden.Present = false;
        Layout(row);

        // Same as if it were never added: one gap, not two.
        Assert.Equal(230f, flexible.ArrangedBounds.Width, 3);
    }
}