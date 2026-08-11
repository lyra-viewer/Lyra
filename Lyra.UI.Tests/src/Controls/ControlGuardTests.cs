using Lyra.UI.Components.Controls;
using Lyra.UI.Components.Layout;
using Lyra.UI.SupportingTypes;
using SkiaSharp;
using Xunit;

namespace Lyra.UI.Tests.Controls;

/// <summary>
/// Degenerate inputs that produce silent nonsense rather than an error.
///
/// Float division by zero does not throw in C# - it yields NaN - so a slider with an empty
/// range used to render its thumb and notches at NaN coordinates, drawing nothing and
/// reporting no fault. Integer division does throw, so a zero-column grid took down the
/// render loop instead.
/// </summary>
public class ControlGuardTests
{
    // --------------------------------------------------------
    //  ValueSlider
    // --------------------------------------------------------

    [Theory]
    [InlineData(5, 5)]   // empty range
    [InlineData(9, 1)]   // inverted range
    public void SliderRejectsARangeItCannotDivide(int min, int max)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new ValueSlider(min, max, min));
    }

    [Fact]
    public void SliderAcceptsTheSmallestUsableRange()
    {
        var slider = new ValueSlider(0, 1, 0);

        Assert.Equal(0, slider.Value);

        slider.Value = 1;
        Assert.Equal(1, slider.Value);
    }

    [Fact]
    public void SliderClampsTheInitialValueIntoRange()
    {
        Assert.Equal(9, new ValueSlider(1, 9, 100).Value);
        Assert.Equal(1, new ValueSlider(1, 9, -100).Value);
    }

    [Fact]
    public void SliderIgnoresADragWhenThereIsNoTrackToHit()
    {
        // Arranged far narrower than its end labels, so left and right cross over.
        var slider = new ValueSlider(1, 9, 5);
        var changes = 0;
        slider.ValueChanged += _ => changes++;

        slider.Measure(new SKSize(4, 44));
        slider.Arrange(new SKRect(0, 0, 4, 44));

        // Right lands left of Left, so an unguarded mapping inverts and still
        // produces plausible-looking values. Sweep the whole width.
        for (var x = 0f; x <= 4f; x += 0.5f)
        {
            slider.OnPointerDown(new SKPoint(x, 22));
            slider.OnPointerMove(new SKPoint(x, 22));
            slider.OnPointerUp(new SKPoint(x, 22));
        }

        Assert.Equal(5, slider.Value);
        Assert.Equal(0, changes);
    }

    // --------------------------------------------------------
    //  Grid
    // --------------------------------------------------------

    [Theory]
    [InlineData(0)]
    [InlineData(-3)]
    public void GridClampsColumnsToAtLeastOne(int requested)
    {
        var grid = new Grid { Columns = requested };

        Assert.Equal(1, grid.Columns);
    }

    [Fact]
    public void GridWithClampedColumnsStillLaysOut()
    {
        var grid = new Grid { Columns = 0 };
        grid.AddComponents(Cell(), Cell(), Cell());

        grid.Measure(new SKSize(200, 200));
        grid.Resolve();
        grid.Arrange(new SKRect(0, 0, 200, 200));

        // One column: three cells stacked, each 20 tall.
        Assert.Equal(60f, grid.DesiredSize.Height);
    }

    private static HStack Cell() => new()
    {
        HorizontalSize = SizeMode.Fixed,
        VerticalSize = SizeMode.Fixed,
        Width = 20,
        Height = 20
    };
}
