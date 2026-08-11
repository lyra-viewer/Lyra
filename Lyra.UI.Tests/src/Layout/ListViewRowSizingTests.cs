using Lyra.UI.Components;
using Lyra.UI.Components.Controls;
using Lyra.UI.Components.Layout;
using Lyra.UI.SupportingTypes;
using SkiaSharp;
using Xunit;

namespace Lyra.UI.Tests.Layout;

/// <summary>
/// ListView forces its rows to be content-sized along the scroll axis, since a row that sized
/// itself to the viewport would break scrolling. That rule used to apply to fixed-height rows as
/// well, which silently flattened spacer rows to nothing - the EXIF panel's group separators were
/// in the data and laid out at zero height, so the panel rendered as one undivided block.
/// </summary>
public class ListViewRowSizingTests
{
    private static readonly SKSize Available = new(400, 1000);

    [Fact]
    public void RowsThatAskForAnExactHeightKeepIt()
    {
        var list = new ListView<int>([0], (_, _) => Spacer(10));

        list.Measure(Available);

        Assert.Equal(10f, list.DesiredSize.Height);
    }

    [Fact]
    public void RowsThatDoNotAskAreStillContentSized()
    {
        // An Expand row inside a scroller would otherwise take the whole viewport.
        var list = new ListView<int>([0], (_, _) => new HStack { VerticalSize = SizeMode.Expand });

        list.Measure(Available);

        Assert.Equal(0f, list.DesiredSize.Height);
    }

    [Fact]
    public void SpacerRowsContributeTheirHeightToTheContentSize()
    {
        // Three spacers plus the row spacing between them - the shape of a separated list.
        var list = new ListView<int>([0, 1, 2], (_, _) => Spacer(10)) { RowSpacing = 2 };

        list.Measure(Available);

        Assert.Equal(34f, list.DesiredSize.Height);
        Assert.Equal(34f, list.ContentSize);
    }

    /// <summary>An empty row whose only job is to occupy height - the EXIF group separator.</summary>
    private static IComponent Spacer(float height) => new HStack
    {
        HorizontalSize = SizeMode.Expand,
        VerticalSize = SizeMode.Fixed,
        Height = height
    };
}
