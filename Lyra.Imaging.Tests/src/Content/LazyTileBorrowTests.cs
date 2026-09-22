using Lyra.Imaging.Content.Tiling;
using SkiaSharp;
using Xunit;

namespace Lyra.Imaging.Tests.Content;

/// <summary>Regression tests for the halve-while-disposing race.</summary>
public class LazyTileBorrowTests
{
    private const int Edge = 256;

    private sealed class GrayTileProvider : ITileProvider
    {
        private readonly List<SKImage> _handedOut = [];
        private readonly Lock _gate = new();

        public int LevelsAboveZeroAsked;

        public SKImage? Decode(int level, int tileX, int tileY, CancellationToken ct)
        {
            if (level > 0)
                Interlocked.Increment(ref LevelsAboveZeroAsked);

            var info = new SKImageInfo(Edge, Edge, SKColorType.Gray8, SKAlphaType.Opaque);
            var pixels = new byte[Edge * Edge];
            Array.Fill(pixels, (byte)(64 + level * 32));

            var image = SKImage.FromPixelCopy(info, pixels);

            lock (_gate)
                _handedOut.Add(image);

            return image;
        }
        
        public int LiveCount
        {
            get
            {
                lock (_gate)
                    return _handedOut.Count(image => image.Handle != IntPtr.Zero);
            }
        }

        public void Dispose() { }
    }

    private static LazyTileSource Build(GrayTileProvider provider, long budget = 64L * 1024 * 1024) =>
        new(tilesX: 2, tilesY: 2, tileWidth: Edge, tileHeight: Edge, provider, budget, bytesPerPixel: 1, maxLevel: 1);

    private static readonly SKRect Whole = SKRect.Create(0, 0, Edge * 2, Edge * 2);
    private static readonly SKSize WholeSize = new(Edge * 2, Edge * 2);

    private static bool WaitFor(Func<bool> condition, TimeSpan? timeout = null)
    {
        var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(10));
        while (DateTime.UtcNow < deadline)
        {
            if (condition())
                return true;

            Thread.Sleep(1);
        }

        return condition();
    }

    [Fact]
    public void CoarseTile_IsHalvedFromTheFinerOnes_WithoutAskingTheProvider()
    {
        var provider = new GrayTileProvider();
        using var source = Build(provider);

        source.GetTiles(Whole, WholeSize, 1f);
        Assert.True(WaitFor(() => source.ResidentCount >= 4), "the full-resolution tiles never arrived");

        var coarse = Array.Empty<RasterTile>();
        Assert.True(
            WaitFor(() =>
            {
                coarse = [.. source.GetTiles(Whole, WholeSize, 0.5f)];
                return coarse.Length > 0;
            }),
            "the coarse tile was never built"
        );

        Assert.Single(coarse);
        Assert.Equal(Edge, coarse[0].Image.Width);
        Assert.Equal(0, provider.LevelsAboveZeroAsked);
    }

    [Fact]
    public void Dispose_DuringACoarseBuild_NeverThrows_AndLeavesNothingResident()
    {
        // Hammer the window between borrowing the four finer tiles and finishing with them.
        for (var iteration = 0; iteration < 150; iteration++)
        {
            var provider = new GrayTileProvider();
            var source = Build(provider);

            source.GetTiles(Whole, WholeSize, 1f);
            if (!WaitFor(() => source.ResidentCount >= 4, TimeSpan.FromSeconds(5)))
            {
                source.Dispose();
                continue; // nothing to race against on this pass
            }

            source.GetTiles(Whole, WholeSize, 0.5f); // queues the halving

            var disposer = Task.Run(source.Dispose);

            // Keep asking while it goes down: drawing does not stop because cleanup started.
            for (var i = 0; i < 8; i++)
                source.GetTiles(Whole, WholeSize, 0.5f);

            disposer.GetAwaiter().GetResult(); // must not throw

            Assert.True(
                WaitFor(() => source.ResidentCount == 0, TimeSpan.FromSeconds(5)),
                $"iteration {iteration}: tiles were still resident after dispose drained"
            );

            Assert.True(
                WaitFor(() => provider.LiveCount == 0, TimeSpan.FromSeconds(5)),
                $"iteration {iteration}: {provider.LiveCount} tile images were never disposed"
            );
        }
    }

    [Fact]
    public void GetTiles_AfterDispose_IsEmpty()
    {
        var provider = new GrayTileProvider();
        var source = Build(provider);

        source.GetTiles(Whole, WholeSize, 1f);
        Assert.True(WaitFor(() => source.ResidentCount >= 4), "the full-resolution tiles never arrived");

        source.Dispose();

        Assert.Equal(0, source.GetTiles(Whole, WholeSize, 1f).Count());
        Assert.Equal(0, source.ResidentCount);
        Assert.True(WaitFor(() => provider.LiveCount == 0), $"{provider.LiveCount} tile images were never disposed");
    }
}