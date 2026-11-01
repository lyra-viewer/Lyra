using Lyra.Imaging.Content;
using Lyra.Imaging.Content.Tiling;
using Lyra.Imaging.Decoding;
using Lyra.Imaging.Tests.Support;
using SkiaSharp;
using Xunit;

namespace Lyra.Imaging.Tests.Content;

/// <summary>
/// Decoded content is measured and disposed from more than one thread. What it reports must not
/// touch pixels another thread may already have released, and it is released once.
/// </summary>
public class ContentLifetimeTests
{
    private static SKImage Image(int width, int height)
    {
        using var bitmap = new SKBitmap(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul));
        bitmap.Erase(SKColors.Gray);
        return SKImage.FromBitmap(bitmap);
    }

    [Fact]
    public void ARaster_KeepsItsSizeAfterDisposal_AndCostsNothing()
    {
        var content = new RasterContent(Image(8, 4));
        Assert.Equal(8 * 4 * 4, content.ByteSize);

        content.Dispose();
        content.Dispose();

        Assert.Equal((8f, 4f), (content.DecodedWidth, content.DecodedHeight));
        Assert.Equal(0, content.ByteSize);
    }

    [Fact]
    public void ARasterMeasuredWhileAnotherThreadDisposesIt_NeverReadsReleasedPixels()
    {
        for (var round = 0; round < 200; round++)
        {
            var content = new RasterContent(Image(16, 16));
            using var start = new Barrier(2);
            var sizes = new List<long>();

            var reader = new Thread(() =>
            {
                start.SignalAndWait();
                for (var i = 0; i < 50; i++)
                    sizes.Add(content.ByteSize + (long)(content.DecodedWidth ?? 0));
            });

            reader.Start();
            start.SignalAndWait();
            content.Dispose();
            reader.Join();

            Assert.All(sizes, size => Assert.True(size is 16 or 16 * 16 * 4 + 16));
        }
    }

    [Fact]
    public void AVector_KeepsItsSizeAfterDisposal()
    {
        using var recorder = new SKPictureRecorder();
        using (var paint = new SKPaint { Color = SKColors.Gray })
            recorder.BeginRecording(SKRect.Create(30, 20)).DrawRect(SKRect.Create(30, 20), paint);

        var content = new VectorContent(recorder.EndRecording());

        content.Dispose();
        content.Dispose();

        Assert.Equal((30f, 20f), (content.DecodedWidth, content.DecodedHeight));
    }

    [Fact]
    public void APublishedLargeRaster_RefusesToBeRebuilt_ButStillCountsTiles()
    {
        using var file = new TempFile([0]);
        using var composite = new Composite(new FileInfo(file.Path));
        var context = DecodeContext.For(composite);

        var content = new RasterLargeContent(4096, 4096, Image(64, 64));
        content.SetTilesTotal(2);

        context.Content = content;

        Assert.True(content.IsSealed);
        Assert.Throws<InvalidOperationException>(() => content.SetPreview(null));
        Assert.Throws<InvalidOperationException>(() => content.SetTiles(new RasterTileSource(1, 1, 64, 64)));

        content.IncrementTileReady();
        Assert.Equal(1, content.TilesReady);
    }

    [Fact]
    public void ALargeRaster_IsReleasedOnce_AndThenCostsNothing()
    {
        var content = new RasterLargeContent(4096, 4096, Image(64, 32));
        var tiles = new RasterTileSource(2, 1, 2048, 4096);
        tiles.SetTile(0, 0, Image(4, 4));
        content.SetTiles(tiles);

        Assert.Equal(64 * 32 * 4 + 4 * 4 * 4, content.ByteSize);

        content.Dispose();
        content.Dispose();

        Assert.Equal(0, content.ByteSize);
        Assert.Equal((64f, 32f), (content.DecodedWidth, content.DecodedHeight));
    }

    [Fact]
    public void TileBytes_FollowTilesAsTheyArriveAndAreReplaced()
    {
        using var tiles = new RasterTileSource(2, 1, 4, 4);

        tiles.SetTile(0, 0, Image(4, 4));
        Assert.Equal(64, tiles.ByteSize);

        tiles.SetTile(0, 0, Image(2, 2));
        Assert.Equal(16, tiles.ByteSize);

        tiles.SetTile(1, 0, Image(4, 4));
        Assert.Equal(80, tiles.ByteSize);

        tiles.Dispose();
        Assert.Equal(0, tiles.ByteSize);
    }
}