using System.Buffers.Binary;
using System.Runtime.InteropServices;
using Lyra.Common.Settings.Enums;
using Lyra.Renderer;
using Lyra.Renderer.Drawing;
using Lyra.UI.Theme;
using SkiaSharp;
using Xunit;

namespace Lyra.Core.Tests.Rendering;

/// <summary>
/// Why the extended-range surface is encoded rather than linear.
///
/// Extended range needs half-float pixels and a color space that permits values above 1.0. It
/// does not need a linear transfer function, and must not have one: Skia gamma-corrects glyph
/// coverage only for an sRGB-like destination, so on a linear surface it blends raw coverage and
/// every glyph in the interface gains about a sixth of its weight. The whole application reads as
/// bold, with no font setting involved.
///
/// These pin both halves: that an encoded surface still carries the headroom, and that it draws
/// text at the weight a display-referred surface does.
/// </summary>
public class SurfaceEncodingTests
{
    private static readonly SKColorSpace P3 = SKColorSpace.CreateRgb(SKColorSpaceTransferFn.Srgb, SKColorSpaceXyz.DisplayP3);

    private static readonly SKColorSpace LinearP3 = SKColorSpace.CreateRgb(new SKColorSpaceTransferFn(1f, 1f, 0f, 0f, 0f, 0f, 0f), SKColorSpaceXyz.DisplayP3);
    
    [Fact]
    public void AnEncodedSurfaceStillCarriesLightAboveWhite()
    {
        var profile = SurfaceProfile.Extended(P3, headroom: 8f);

        Assert.Equal(1.825f, Render(4f, profile, P3, readAs: P3), 2);
        Assert.Equal(4f, Render(4f, profile, P3, readAs: LinearP3), 2);
    }

    [Theory]
    [InlineData(0.25f)]
    [InlineData(0.9f)]
    [InlineData(2f)]
    [InlineData(4.805f)]
    public void EncodedAndLinearSurfacesAgreeOnTheLightTheyCarry(float scene)
    {
        var encoded = Render(scene, SurfaceProfile.Extended(P3, headroom: 8f), P3, readAs: LinearP3);
        var linear = Render(scene, SurfaceProfile.Extended(LinearP3, headroom: 8f), LinearP3, readAs: LinearP3);

        Assert.Equal(linear, encoded, 0.01f);
    }

    [Fact]
    public void ADisplayReferredSurfaceStillStopsAtWhite()
    {
        Assert.Equal(SurfaceProfile.SdrWhite, Render(4f, SurfaceProfile.DisplayReferred(P3), P3, readAs: LinearP3), 2);
    }

    [Fact]
    public void AnExtendedSurfaceNeedNotBeLinear_AndOursIsNot()
    {
        Assert.False(SurfaceProfile.Extended(P3, headroom: 4.8f).IsLinearlyEncoded);
        Assert.True(SurfaceProfile.Extended(LinearP3, headroom: 4.8f).IsLinearlyEncoded);
        Assert.False(SurfaceProfile.Unknown.IsLinearlyEncoded);
    }
    
    [Theory]
    [InlineData(11f)]
    [InlineData(14f)]
    public void TextDrawnIntoALinearSurfaceGainsWeight(float size)
    {
        var encoded = Ink(DrawInterface(size, SKColorType.Bgra8888, P3));
        var linear = Ink(DrawInterface(size, SKColorType.RgbaF16, LinearP3));

        Assert.True(linear > encoded * 1.08f, $"expected a linear surface to fatten the text; got {linear:F2} against {encoded:F2}.");
    }
    
    [Theory]
    [InlineData(11f)]
    [InlineData(14f)]
    public void TheExtendedSurfaceDrawsTextAtItsOrdinaryWeight(float size)
    {
        var displayReferred = Ink(DrawInterface(size, SKColorType.Bgra8888, P3));
        var extended = Ink(DrawInterface(size, SKColorType.RgbaF16, P3));

        // Half-float precision against 8-bit is the whole of the difference.
        Assert.Equal(displayReferred, extended, 0.02 * displayReferred);
    }

    /// <summary>
    /// Interface white lands on SDR white exactly, on the surface as it is really configured.
    /// </summary>
    [Fact]
    public void InterfaceWhiteIsSdrWhiteOnTheEncodedSurface()
    {
        using var surface = SKSurface.Create(new SKImageInfo(1, 1, SKColorType.RgbaF16, SKAlphaType.Unpremul, P3));

        using var paint = new SKPaint();
        paint.Color = SKColors.White;
        surface.Canvas.DrawRect(new SKRect(0, 0, 1, 1), paint);

        Assert.Equal(SurfaceProfile.SdrWhite, ReadRed(surface, LinearP3), 2);
    }

    private const int Width = 220;
    private const int Height = 24;

    private static readonly SKColor Background = new(38, 40, 43);
    private static readonly SKColor Foreground = new(205, 208, 212);

    /// <summary>Draws the interface's own text into a surface and reads it back as 8-bit sRGB.</summary>
    private static SKBitmap DrawInterface(float size, SKColorType colorType, SKColorSpace space)
    {
        BundledFonts.Register();

        using var surface = SKSurface.Create(new SKImageInfo(Width, Height, colorType, SKAlphaType.Premul, space));
        surface.Canvas.Clear(Background);

        var typeface = Fonts.Resolve(BundledFonts.MonospaceFamily, SKFontStyleWeight.Normal, SKFontStyleSlant.Upright);

        Assert.Equal(BundledFonts.MonospaceFamily, typeface.FamilyName);

        using var font = new SKFont(typeface, size);
        using var paint = new SKPaint();
        paint.Color = Foreground;
        paint.IsAntialias = true;

        surface.Canvas.DrawText("Size Range 16 x 16", 4, 16, SKTextAlign.Left, font, paint);

        using var snapshot = surface.Snapshot();
        var readback = new SKBitmap(new SKImageInfo(Width, Height, SKColorType.Rgba8888, SKAlphaType.Unpremul, SKColorSpace.CreateSrgb()));

        Assert.True(snapshot.ReadPixels(readback.Info, readback.GetPixels(), readback.RowBytes, 0, 0));

        return readback;
    }

    private static double Ink(SKBitmap bitmap)
    {
        using (bitmap)
        {
            double total = 0;

            for (var y = 0; y < bitmap.Height; y++)
            for (var x = 0; x < bitmap.Width; x++)
                total += Math.Max(0, bitmap.GetPixel(x, y).Red - Background.Red);

            return total / (bitmap.Width * bitmap.Height);
        }
    }

    private static float Render(float scene, SurfaceProfile profile, SKColorSpace surfaceSpace, SKColorSpace readAs)
    {
        using var image = LinearPixel(scene);
        using var paint = HdrToneMapShader.CreatePaint(image, new SKSamplingOptions(SKFilterMode.Nearest), SKMatrix.CreateIdentity(), ToneMapMode.Clip, exposureScale: 1f, whitePoint: 1f, profile);

        Assert.NotNull(paint);

        using var surface = SKSurface.Create(new SKImageInfo(1, 1, SKColorType.RgbaF16, SKAlphaType.Unpremul, surfaceSpace));
        surface.Canvas.DrawRect(new SKRect(0, 0, 1, 1), paint);

        return ReadRed(surface, readAs);
    }

    private static float ReadRed(SKSurface surface, SKColorSpace readAs)
    {
        var info = new SKImageInfo(1, 1, SKColorType.RgbaF16, SKAlphaType.Unpremul, readAs);
        var buffer = new byte[8];

        using var snapshot = surface.Snapshot();
        var handle = GCHandle.Alloc(buffer, GCHandleType.Pinned);

        try
        {
            Assert.True(snapshot.ReadPixels(info, handle.AddrOfPinnedObject(), 8, 0, 0));
        }
        finally
        {
            handle.Free();
        }

        return (float)BinaryPrimitives.ReadHalfLittleEndian(buffer);
    }

    private static SKImage LinearPixel(float value)
    {
        var info = new SKImageInfo(1, 1, SKColorType.RgbaF16, SKAlphaType.Unpremul, SKColorSpace.CreateSrgbLinear());
        var bitmap = new SKBitmap(info);

        var pixel = new byte[8];
        for (var channel = 0; channel < 3; channel++)
            BitConverter.GetBytes((Half)value).CopyTo(pixel, channel * 2);
        
        BitConverter.GetBytes((Half)1f).CopyTo(pixel, 6);

        Marshal.Copy(pixel, 0, bitmap.GetPixels(), pixel.Length);
        bitmap.SetImmutable();

        return SKImage.FromBitmap(bitmap);
    }
}