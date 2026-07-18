using Lyra.Imaging.Content;
using SkiaSharp;
using Xunit;

namespace Lyra.Imaging.Tests.Content;

/// <summary>
/// Regression tests for the dispose-while-streaming race: background PSD tile decode can call
/// SetTile after cleanup disposed the tile source (previously an IndexOutOfRangeException,
/// because Dispose swapped the slot array for an empty one while the bounds check still used
/// the original dimensions). Late tiles must be dropped and disposed, never thrown on.
/// </summary>
public class RasterTileSourceDisposeTests
{
    private static SKImage MakeTile() => SKImage.FromPixelCopy(new SKImageInfo(1, 1, SKColorType.Rgba8888, SKAlphaType.Premul), new byte[4]);

    [Fact]
    public void SetTile_AfterDispose_DropsAndDisposesTile()
    {
        var source = new RasterTileSource(tilesX: 4, tilesY: 4, tileWidth: 64, tileHeight: 64);
        source.Dispose();

        var tile = MakeTile();
        source.SetTile(1, 1, tile); // must not throw

        Assert.Equal(IntPtr.Zero, tile.Handle); // ownership taken: dropped tile was disposed
        Assert.Empty(source.GetTiles(SKRect.Create(0, 0, 256, 256), new SKSize(256, 256)));
    }

    [Fact]
    public void SetTile_OutOfRange_StillThrows()
    {
        var source = new RasterTileSource(tilesX: 2, tilesY: 2, tileWidth: 64, tileHeight: 64);
        using var tile = MakeTile();

        Assert.Throws<ArgumentOutOfRangeException>(() => source.SetTile(2, 0, tile));
        source.Dispose();
    }

    [Fact]
    public void ConcurrentSetTileAndDispose_NeverThrows_AndDisposesEveryImage()
    {
        // Hammer the race window: writers stream tiles while Dispose runs concurrently.
        // Every image handed to SetTile must end up disposed regardless of who wins each slot.
        for (var iteration = 0; iteration < 200; iteration++)
        {
            var source = new RasterTileSource(tilesX: 8, tilesY: 8, tileWidth: 16, tileHeight: 16);
            var images = new List<SKImage>[4];
            using var start = new ManualResetEventSlim(false);

            var writers = Enumerable.Range(0, images.Length).Select(w => Task.Run(() =>
            {
                images[w] = [];
                var rng = new Random(w * 1000 + iteration);
                start.Wait();

                for (var i = 0; i < 32; i++)
                {
                    var img = MakeTile();
                    images[w].Add(img);
                    source.SetTile(rng.Next(8), rng.Next(8), img);
                }
            })).ToArray();

            var disposer = Task.Run(() =>
            {
                start.Wait();
                source.Dispose();
            });

            start.Set();
            Task.WaitAll([.. writers, disposer]);

            foreach (var img in images.SelectMany(list => list))
                Assert.Equal(IntPtr.Zero, img.Handle);
        }
    }
}