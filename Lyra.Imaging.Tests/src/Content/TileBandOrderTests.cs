using Lyra.Imaging.Content.Tiling;
using Xunit;

namespace Lyra.Imaging.Tests.Content;

/// <summary>
/// The order a tiled image's bands are decoded in. It spirals outward from the middle of the
/// grid, so a sheet resolves where it is most likely to be looked at first rather than from the
/// top edge down.
/// </summary>
public class TileBandOrderTests
{
    [Theory]
    [InlineData(1, 1)]
    [InlineData(4, 1)]
    [InlineData(1, 7)]
    [InlineData(3, 9)]
    [InlineData(8, 4)]
    [InlineData(13, 21)]
    public void EveryBandIsScheduledExactlyOnce(int tilesX, int tilesY)
    {
        var order = TileDecodeScheduler.BuildBandOrder(tilesX, tilesY);

        Assert.Equal(tilesY, order.Count);
        Assert.Equal(Enumerable.Range(0, tilesY), order.Order());
    }

    [Fact]
    public void TheMiddleBandComesFirst()
    {
        Assert.Equal(3, TileDecodeScheduler.BuildBandOrder(4, 7)[0]);
        Assert.Equal(4, TileDecodeScheduler.BuildBandOrder(4, 8)[0]);
    }

    [Fact]
    public void BandsArriveOutwardFromTheMiddle()
    {
        // A band's place in the queue never improves as it gets further from the centre.
        var order = TileDecodeScheduler.BuildBandOrder(5, 9);
        var centre = 9 / 2;

        for (var position = 1; position < order.Count; position++)
        {
            var here = Math.Abs(order[position] - centre);
            var nearest = order.Take(position).Min(band => Math.Abs(band - centre));

            Assert.True(here >= nearest, $"band {order[position]} scheduled at {position} is closer in than one already queued");
        }
    }

    [Theory]
    [InlineData(0, 4)]
    [InlineData(4, 0)]
    [InlineData(-1, -1)]
    public void ADegenerateGridSchedulesNothing(int tilesX, int tilesY)
    {
        Assert.Empty(TileDecodeScheduler.BuildBandOrder(tilesX, tilesY));
    }
}