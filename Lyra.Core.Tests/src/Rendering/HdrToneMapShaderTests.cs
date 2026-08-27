using Lyra.Common.Settings.Enums;
using Lyra.Renderer.Drawing;
using SkiaSharp;
using Xunit;

namespace Lyra.Core.Tests.Rendering;

/// <summary>
/// The shader source is an embedded resource, so two things the compiler cannot check: that it is
/// present at all, and that the uniforms it declares are the ones the C# side uploads.
///
/// Both failures are quiet - the effect goes null and the draw falls back to an unmapped image,
/// which looks like a driver problem on someone else's machine.
/// </summary>
public class HdrToneMapShaderTests
{
    /// <summary>
    /// Every uniform the shader declares, and nothing else, is what `CreatePaint` sets.
    /// </summary>
    [Fact]
    public void TheShaderDeclaresExactlyTheUniformsTheRendererUploads()
    {
        var effect = Compile();

        string[] uploaded =
        [
            "exposure",
            "whitePoint",
            "mode",
            "gamut",
            "ceiling",
            "lumaWeights",
            "encodeGABC",
            "encodeDEF"
        ];

        Assert.Equal(uploaded.OrderBy(name => name), effect.Uniforms.OrderBy(name => name));
    }
    
    [Fact]
    public void TheEmbeddedShaderCompiles()
    {
        var effect = Compile();

        Assert.NotNull(effect);
        Assert.True(HdrToneMapShader.IsAvailable, "the renderer should report the effect as usable.");
    }

    [Fact]
    public void TheEmbeddedShaderStillProducesTheRightPixel()
    {
        using var image = LinearPixel(0.5f);
        using var paint = HdrToneMapShader.CreatePaint(image, new SKSamplingOptions(SKFilterMode.Nearest), SKMatrix.CreateIdentity(), ToneMapMode.Clip, exposureScale: 1f, whitePoint: 1f, SurfaceProfile.DisplayReferred(SKColorSpace.CreateSrgb()));

        Assert.NotNull(paint);

        var info = new SKImageInfo(1, 1, SKColorType.Rgba8888, SKAlphaType.Unpremul, SKColorSpace.CreateSrgb());
        using var surface = SKSurface.Create(info);
        surface.Canvas.DrawRect(new SKRect(0, 0, 1, 1), paint);

        using var snapshot = surface.Snapshot();
        using var readback = new SKBitmap(info);
        Assert.True(snapshot.ReadPixels(readback.Info, readback.GetPixels(), readback.RowBytes, 0, 0));

        // Linear 0.5 through the sRGB encode, the same number the domain tests pin.
        Assert.Equal(188, readback.GetPixel(0, 0).Red);
    }

    /// <summary>
    /// A translucent pixel comes back at the brightness it went in at.
    /// </summary>
    [Fact]
    public void ATranslucentPixelIsNotBrightenedByItsOwnAlpha()
    {
        using var image = LinearPixel(0.5f, alpha: 0.5f);
        using var paint = HdrToneMapShader.CreatePaint(image, new SKSamplingOptions(SKFilterMode.Nearest), SKMatrix.CreateIdentity(), ToneMapMode.Clip, exposureScale: 1f, whitePoint: 1f, SurfaceProfile.DisplayReferred(SKColorSpace.CreateSrgb()));

        Assert.NotNull(paint);

        // Premultiplied destination, as every real render target is.
        var target = new SKImageInfo(1, 1, SKColorType.Rgba8888, SKAlphaType.Premul, SKColorSpace.CreateSrgb());
        using var surface = SKSurface.Create(target);
        surface.Canvas.Clear(SKColors.Transparent);
        surface.Canvas.DrawRect(new SKRect(0, 0, 1, 1), paint);

        using var snapshot = surface.Snapshot();
        using var readback = new SKBitmap(target.WithAlphaType(SKAlphaType.Unpremul));
        Assert.True(snapshot.ReadPixels(readback.Info, readback.GetPixels(), readback.RowBytes, 0, 0));

        var pixel = readback.GetPixel(0, 0);

        // The same 188 the opaque case produces: alpha carries the transparency, not the colour.
        Assert.Equal(128, pixel.Alpha);
        Assert.InRange(pixel.Red, 186, 190);
    }

    private static SKRuntimeEffect Compile()
    {
        using var stream = typeof(HdrToneMapShader).Assembly.GetManifestResourceStream("LyraViewer.Shaders.HdrToneMap.sksl");

        Assert.NotNull(stream);

        using var reader = new StreamReader(stream);
        var effect = SKRuntimeEffect.CreateShader(reader.ReadToEnd(), out var errors);

        Assert.True(string.IsNullOrEmpty(errors), $"the shader failed to compile: {errors}");
        Assert.NotNull(effect);

        return effect;
    }

    private static SKImage LinearPixel(float value, float alpha = 1f)
    {
        var info = new SKImageInfo(1, 1, SKColorType.RgbaF16, SKAlphaType.Unpremul, SKColorSpace.CreateSrgbLinear());
        var bitmap = new SKBitmap(info);

        var pixel = new byte[8];
        for (var channel = 0; channel < 3; channel++)
            BitConverter.GetBytes((Half)value).CopyTo(pixel, channel * 2);

        BitConverter.GetBytes((Half)alpha).CopyTo(pixel, 6);

        System.Runtime.InteropServices.Marshal.Copy(pixel, 0, bitmap.GetPixels(), pixel.Length);
        bitmap.SetImmutable();

        return SKImage.FromBitmap(bitmap);
    }
}