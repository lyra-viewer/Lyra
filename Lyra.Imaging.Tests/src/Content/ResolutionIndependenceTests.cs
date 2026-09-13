using Lyra.Imaging.Content;
using SkiaSharp;
using Xunit;

namespace Lyra.Imaging.Tests.Content;

public class ResolutionIndependenceTests
{
    [Fact]
    public void Vector_IsResolutionIndependent()
    {
        using var picture = RecordPicture(64, 64);
        using var content = new VectorContent(picture);

        Assert.True(content.IsResolutionIndependent);
    }

    [Fact]
    public void Raster_IsNot()
    {
        using var content = MakeRaster();

        Assert.False(content.IsResolutionIndependent);
    }

    [Fact]
    public void HdrRaster_IsNot_BecauseItIsStillPixels()
    {
        using var bitmap = new SKBitmap(new SKImageInfo(4, 4, SKColorType.RgbaF16, SKAlphaType.Premul));
        using var image = SKImage.FromBitmap(bitmap);
        using var content = new HdrRasterContent(bitmap, image, whitePoint: 1f);

        Assert.False(content.IsResolutionIndependent);
    }

    [Fact]
    public void RasterLarge_IsNot()
    {
        using var content = new RasterLargeContent(8000, 6000);

        Assert.False(content.IsResolutionIndependent);
    }

    [Fact]
    public void Variant_FollowsTheActiveRendition()
    {
        using var picture = RecordPicture(32, 32);

        var contents = new List<ICompositeContent> { MakeRaster(), new VectorContent(picture) };
        var variants = new List<ImageVariant>
        {
            new("raster", 4, 4, "", 0),
            new("vector", 32, 32, "", 0)
        };

        using var content = new VariantRasterContent(variants, contents, active: 0);
        Assert.False(content.IsResolutionIndependent);

        Assert.True(content.Select(1));
        Assert.True(content.IsResolutionIndependent);
    }

    private static RasterContent MakeRaster()
    {
        var bitmap = new SKBitmap(new SKImageInfo(4, 4, SKColorType.Rgba8888, SKAlphaType.Premul));
        return new RasterContent(bitmap, SKImage.FromBitmap(bitmap));
    }

    private static SKPicture RecordPicture(int width, int height)
    {
        using var recorder = new SKPictureRecorder();
        var canvas = recorder.BeginRecording(new SKRect(0, 0, width, height));
        canvas.Clear(SKColors.Black);
        return recorder.EndRecording();
    }
}