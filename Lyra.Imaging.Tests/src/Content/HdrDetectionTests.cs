using Lyra.Imaging.Content;
using SkiaSharp;
using Xunit;

namespace Lyra.Imaging.Tests.Content;

/// <summary>
/// Whether a composite counts as live HDR, which decides whether the HDR panel shows its controls.
/// A page set is judged by the page on screen, since that is what the controls act on.
/// </summary>
public class HdrDetectionTests : IDisposable
{
    private readonly Composite _composite = new(new FileInfo(Path.Combine(Path.GetTempPath(), "lyra-hdr-detect.tif")));

    private static ImageVariant Page(int n) => new($"Page {n}", 1, 1, "float", 16);

    private static RasterContent Sdr()
    {
        var bitmap = new SKBitmap(new SKImageInfo(1, 1, SKColorType.Rgba8888, SKAlphaType.Premul));
        return new RasterContent(bitmap, SKImage.FromBitmap(bitmap));
    }

    private static HdrRasterContent Hdr()
    {
        var bitmap = new SKBitmap(new SKImageInfo(1, 1, SKColorType.RgbaF16, SKAlphaType.Unpremul));
        return new HdrRasterContent(bitmap, SKImage.FromBitmap(bitmap), whitePoint: 4f);
    }

    [Fact]
    public void AnHdrPageInAPageSetCountsAsLiveHdr()
    {
        _composite.Content = new VariantRasterContent([Page(1), Page(2)], [Hdr(), Hdr()], active: 0);

        Assert.True(_composite.IsHdrDecoded);
        Assert.True(_composite.IsHdrImage);
    }

    [Fact]
    public void AnSdrPageSetIsNot()
    {
        _composite.Content = new VariantRasterContent([Page(1), Page(2)], [Sdr(), Sdr()], active: 0);

        Assert.False(_composite.IsHdrDecoded);
        Assert.False(_composite.IsHdrImage);
    }

    [Fact]
    public void ItFollowsThePageOnScreen()
    {
        var set = new VariantRasterContent([Page(1), Page(2)], [Sdr(), Hdr()], active: 0);
        _composite.Content = set;

        Assert.False(_composite.IsHdrDecoded);

        set.Select(1);

        Assert.True(_composite.IsHdrDecoded);
    }

    [Fact]
    public void PlainHdrContentStillCounts()
    {
        _composite.Content = Hdr();

        Assert.True(_composite.IsHdrDecoded);
    }

    public void Dispose() => _composite.Dispose();
}
