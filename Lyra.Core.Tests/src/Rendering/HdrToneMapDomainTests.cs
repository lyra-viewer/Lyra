using System.Runtime.InteropServices;
using Lyra.Common.Settings.Enums;
using Lyra.Renderer.Drawing;
using SkiaSharp;
using Xunit;

namespace Lyra.Core.Tests.Rendering;

/// <summary>
/// The domain the tone curve runs in, which is the whole correctness of the HDR draw path: every
/// tone curve is defined on scene-linear light, and a runtime effect's child shader is evaluated
/// in the destination color space unless sampled raw.
///
/// The numbers are hand-computable from the curve and the encode, so a regression says by how much.
/// </summary>
public class HdrToneMapDomainTests
{
    private static readonly SKColorSpace LinearSrgb = SKColorSpace.CreateSrgbLinear();
    private static readonly SKColorSpace Srgb = SKColorSpace.CreateSrgb();
    private static readonly SKColorSpace DisplayP3 = SKColorSpace.CreateRgb(SKColorSpaceTransferFn.Srgb, SKColorSpaceXyz.DisplayP3);
    
    [Fact]
    public void TheShaderReceivesLinearLight_NotEncodedValues()
    {
        var read = Draw(0.5f, ToneMapMode.Clip, exposure: 1f, destination: Srgb);

        Assert.Equal(Expected(0.5f), read);
        Assert.Equal(188, read);
    }

    /// <summary>
    /// Shadows are where the encode choice shows. The sRGB curve and a plain 2.2 power curve cross
    /// around a linear 0.1, nearly coinciding above and separating below. The surface declares
    /// sRGB, so sRGB is what belongs in it.
    /// </summary>
    [Fact]
    public void ShadowsUseTheTransferTheSurfaceDeclares()
    {
        var read = Draw(0.01f, ToneMapMode.Clip, exposure: 1f, destination: Srgb);

        Assert.Equal(Expected(0.01f), read);
        Assert.Equal(25, read);

        // What a plain 2.2 encode would write into a surface that reads it back as sRGB.
        Assert.Equal(31, (int)MathF.Round(MathF.Pow(0.01f, 1f / 2.2f) * 255f));
    }
    
    [Fact]
    public void ALinearDestinationIsNotEncodedAtAll()
    {
        var read = Draw(0.5f, ToneMapMode.Clip, exposure: 1f, destination: LinearSrgb);

        Assert.Equal(128, read);
    }

    /// Values above SDR white must survive into the curve, which is why the content is kept as
    /// half-float. Scaled down by exposure, a 4.0 lands where a linear pipeline says.
    [Fact]
    public void HeadroomSurvivesIntoTheCurve()
    {
        var read = Draw(4f, ToneMapMode.Clip, exposure: 0.25f, destination: Srgb);

        Assert.Equal(255, read);
        Assert.Equal(Expected(4f * 0.5f), Draw(4f, ToneMapMode.Clip, exposure: 0.5f, destination: Srgb));
    }

    /// A stop is a doubling of light, which is only true if the multiply happens on linear values.
    [Fact]
    public void AStopIsAStop()
    {
        Assert.Equal(Expected(0.25f), Draw(0.25f, ToneMapMode.Clip, exposure: 1f, destination: Srgb));
        Assert.Equal(Expected(0.5f), Draw(0.25f, ToneMapMode.Clip, exposure: 2f, destination: Srgb));
        Assert.Equal(Expected(0.125f), Draw(0.25f, ToneMapMode.Clip, exposure: 0.5f, destination: Srgb));
    }

    /// Each curve lands where its own formula says on linear input, and the three disagree with
    /// each other.
    [Fact]
    public void EachCurveLandsWhereItsFormulaSaysOnLinearInput()
    {
        const float x = 0.5f;
        const float aces = (x * (2.51f * x + 0.03f)) / (x * (2.43f * x + 0.59f) + 0.14f);
        const float white = 4f;
        const float reinhard = x * (1f + x / (white * white)) / (1f + x);

        Assert.Equal(Expected(x), Draw(x, ToneMapMode.Clip, exposure: 1f, destination: Srgb));
        Assert.Equal(Expected(aces), Draw(x, ToneMapMode.Aces, exposure: 1f, destination: Srgb), tolerance: 1);
        Assert.Equal(Expected(reinhard), Draw(x, ToneMapMode.ReinhardExtended, exposure: 1f, destination: Srgb, whitePoint: white), tolerance: 1);
    }

    /// <summary>
    /// Reinhard extended with a white point of 1.0 is algebraically the identity - x(1+x)/(1+x) -
    /// so it agrees with Clip below white. Hence, the draw path measures the image's own white
    /// point: at 1 the selected curve would do nothing.
    /// </summary>
    [Fact]
    public void ReinhardNeedsARealWhitePointToDoAnything()
    {
        var atOne = Draw(0.5f, ToneMapMode.ReinhardExtended, exposure: 1f, destination: Srgb, whitePoint: 1f);
        var atFour = Draw(0.5f, ToneMapMode.ReinhardExtended, exposure: 1f, destination: Srgb, whitePoint: 4f);

        Assert.Equal(Draw(0.5f, ToneMapMode.Clip, exposure: 1f, destination: Srgb), atOne);
        Assert.NotEqual(atOne, atFour);
    }
    
    [Fact]
    public void AMatchingGamutIsLeftAlone()
    {
        Assert.Equal(Expected(0.5f), Draw(0.5f, ToneMapMode.Clip, exposure: 1f, destination: LinearSrgb.ToSrgbTagged()));
    }
    
    [Fact]
    public void NeutralStaysNeutralAcrossGamuts()
    {
        var (r, g, b) = DrawRgb([0.5f, 0.5f, 0.5f], ToneMapMode.Clip, exposure: 1f, destination: DisplayP3);

        Assert.Equal(r, g);
        Assert.Equal(g, b);
        Assert.Equal(Expected(0.5f), r);
    }

    /// <summary>
    /// Rec.709 primaries into a wider Display-P3 surface: the same red needs less saturated
    /// coordinates there, so it comes down in red and picks up a little of both other channels.
    /// </summary>
    [Fact]
    public void WiderPrimariesPullPureRedInwards()
    {
        var (r, g, b) = DrawRgb([0.5f, 0f, 0f], ToneMapMode.Clip, exposure: 1f, destination: DisplayP3);

        Assert.True(r < Expected(0.5f), $"red should come down in P3, got {r}.");
        Assert.True(r > g && r > b, $"red must stay dominant, got {r}/{g}/{b}.");
        Assert.True(g > 0, "P3 red needs a green component to reproduce a Rec.709 red.");
        Assert.True(b > 0, $"a transposed matrix leaves blue at zero here; got {b}.");
        Assert.True(g > b, $"green should exceed blue for a red primary, got {g}/{b}.");
    }
    
    private static int Expected(float linear)
    {
        var x = Math.Clamp(linear, 0f, 1f);
        var encoded = x <= 0.0031308f ? x * 12.92f : (1.055f * MathF.Pow(x, 1f / 2.4f)) - 0.055f;

        return (int)MathF.Round(encoded * 255f);
    }

    private static int Draw(float linear, ToneMapMode mode, float exposure, SKColorSpace destination, float whitePoint = 1f)
        => DrawRgb([linear, linear, linear], mode, exposure, destination, whitePoint).R;

    private static (int R, int G, int B) DrawRgb(float[] rgb, ToneMapMode mode, float exposure, SKColorSpace destination, float whitePoint = 1f)
    {
        using var image = LinearPixel(rgb);
        using var paint = HdrToneMapShader.CreatePaint(image, new SKSamplingOptions(SKFilterMode.Nearest), SKMatrix.CreateIdentity(), mode, exposure, whitePoint, SurfaceProfile.DisplayReferred(destination));

        Assert.NotNull(paint);

        var info = new SKImageInfo(1, 1, SKColorType.Rgba8888, SKAlphaType.Unpremul, destination);
        using var surface = SKSurface.Create(info);
        surface.Canvas.DrawRect(new SKRect(0, 0, 1, 1), paint);

        using var snapshot = surface.Snapshot();
        using var readback = new SKBitmap(info);
        Assert.True(snapshot.ReadPixels(readback.Info, readback.GetPixels(), readback.RowBytes, 0, 0));

        var pixel = readback.GetPixel(0, 0);
        return (pixel.Red, pixel.Green, pixel.Blue);
    }
    
    private static SKImage LinearPixel(float[] rgb)
    {
        var info = new SKImageInfo(1, 1, SKColorType.RgbaF16, SKAlphaType.Unpremul, LinearSrgb);
        var bitmap = new SKBitmap(info);

        var pixel = new byte[8];
        for (var channel = 0; channel < 3; channel++)
            BitConverter.GetBytes((Half)rgb[channel]).CopyTo(pixel, channel * 2);

        BitConverter.GetBytes((Half)1f).CopyTo(pixel, 6);

        Marshal.Copy(pixel, 0, bitmap.GetPixels(), pixel.Length);
        bitmap.SetImmutable();

        return SKImage.FromBitmap(bitmap);
    }
}

file static class ColorSpaceExtensions
{
    public static SKColorSpace ToSrgbTagged(this SKColorSpace _) => SKColorSpace.CreateRgb(SKColorSpaceTransferFn.Srgb, SKColorSpaceXyz.Srgb);
}