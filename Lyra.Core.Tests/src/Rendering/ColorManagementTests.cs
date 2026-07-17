using SkiaSharp;
using Xunit;

namespace Lyra.Core.Tests.Rendering;

/// <summary>
/// Guards the color-management contract the decoders and renderers depend on: images are
/// tagged with their real gamut and the render surface carries the display gamut, so Skia
/// converts image -> surface at draw time. A Display-P3 surface (macOS) preserves wide-gamut
/// detail; an sRGB surface (Linux/X11) folds it down. These are pure SkiaSharp behavior tests
/// (no native decoders) that would fail if someone re-introduced a decode-time sRGB clamp or
/// dropped the surface color space. See also the JXL decode regression test.
/// </summary>
public class ColorManagementTests
{
    private static readonly SKColorSpace DisplayP3 = SKColorSpace.CreateRgb(SKColorSpaceTransferFn.Srgb, SKColorSpaceXyz.DisplayP3);

    private static SKImage MakeWideGamutTwoReds()
    {
        var bitmap = new SKBitmap(new SKImageInfo(2, 1, SKColorType.Rgba8888, SKAlphaType.Premul, DisplayP3));
        bitmap.SetPixel(0, 0, new SKColor(255, 0, 0));

        using (var srgbPixel = new SKBitmap(new SKImageInfo(1, 1, SKColorType.Rgba8888, SKAlphaType.Premul, SKColorSpace.CreateSrgb())))
        {
            srgbPixel.SetPixel(0, 0, new SKColor(255, 0, 0));
            using var srgbImage = SKImage.FromBitmap(srgbPixel);
            using var canvas = new SKCanvas(bitmap);
            canvas.DrawImage(srgbImage, new SKRect(1, 0, 2, 1)); // sRGB red -> P3 encoding
        }

        return SKImage.FromBitmap(bitmap);
    }

    private static SKImage MakeSolid(SKColor color, SKColorSpace? colorSpace)
    {
        var bitmap = new SKBitmap(new SKImageInfo(2, 1, SKColorType.Rgba8888, SKAlphaType.Premul, colorSpace));
        bitmap.SetPixel(0, 0, color);
        bitmap.SetPixel(1, 0, color);
        return SKImage.FromBitmap(bitmap);
    }

    private static (SKColor px0, SKColor px1) DrawAndReadBack(SKImage image, SKColorSpace surfaceColorSpace)
    {
        var info = new SKImageInfo(2, 1, SKColorType.Rgba8888, SKAlphaType.Premul, surfaceColorSpace);
        using var surface = SKSurface.Create(info);
        surface.Canvas.DrawImage(image, 0, 0);

        using var snapshot = surface.Snapshot();
        using var readback = new SKBitmap(info);
        Assert.True(snapshot.ReadPixels(readback.Info, readback.GetPixels(), readback.RowBytes, 0, 0));
        return (readback.GetPixel(0, 0), readback.GetPixel(1, 0));
    }

    [Fact]
    public void DisplayP3Surface_PreservesWideGamutReds()
    {
        using var image = MakeWideGamutTwoReds();
        var (logo, background) = DrawAndReadBack(image, DisplayP3);

        Assert.NotEqual(logo, background);
    }

    [Fact]
    public void SrgbSurface_FoldsWideGamutRedsTogether()
    {
        using var image = MakeWideGamutTwoReds();
        var (logo, background) = DrawAndReadBack(image, SKColorSpace.CreateSrgb());

        // Targeting sRGB clamps the out-of-gamut P3 red onto sRGB red: the two collapse.
        // (This is correct for an sRGB display, which physically cannot show the difference.)
        Assert.Equal(logo, background);
    }

    [Fact]
    public void UntaggedImage_OnDisplayP3Surface_IsTreatedAsSrgb()
    {
        using var untagged = MakeSolid(new SKColor(0, 255, 0), colorSpace: null);
        using var srgbTagged = MakeSolid(new SKColor(0, 255, 0), SKColorSpace.CreateSrgb());

        var untaggedResult = DrawAndReadBack(untagged, DisplayP3).px0;
        var srgbResult = DrawAndReadBack(srgbTagged, DisplayP3).px0;

        Assert.Equal(srgbResult, untaggedResult);
    }
}