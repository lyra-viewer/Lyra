using Lyra.Common.Settings.Enums;
using Lyra.Imaging.Content;
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
            "sceneWhite",
            "peak",
            "encoding",
            "toneLutSize",
            "toWorking",
            "fromWorking",
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

    [Fact]
    public void WithoutASceneRange_SaturationStaysBelowWhite()
    {
        Assert.InRange(DrawSdr(2f, ToneMapMode.Aces, default), 240, 250);
    }

    [Fact]
    public void ASceneWhite_LandsOnWhite()
    {
        Assert.Equal(255, DrawSdr(2f, ToneMapMode.Aces, new SceneRange(SceneWhite: 2f, Peak: 4f)));
    }

    [Theory]
    [InlineData(ToneMapMode.ReinhardExtended, 0.75f)]
    [InlineData(ToneMapMode.Clip, 0.75f)]
    [InlineData(ToneMapMode.ReinhardExtended, 2.5f)]
    [InlineData(ToneMapMode.Clip, 2.5f)]
    public void ASceneWhite_LandsOnWhiteUnderEveryCurve(ToneMapMode mode, float sceneWhite)
    {
        Assert.Equal(255, DrawSdr(sceneWhite, mode, new SceneRange(SceneWhite: sceneWhite, Peak: 4f)));
    }

    [Fact]
    public void TheCamerasOwnCurve_ReplacesAcesOnly()
    {
        using var curve = new CameraToneCurve([.. Enumerable.Range(0, 1024).Select(i => MathF.Pow(i / 1023f, 2f))]);
        var range = new SceneRange(Curve: curve);

        // 0.5 squared is 0.25, which sRGB encodes as 137; the same as the CPU path.
        Assert.InRange(DrawSdr(0.5f, ToneMapMode.Aces, range), 136, 138);
        Assert.Equal(188, DrawSdr(0.5f, ToneMapMode.Clip, range));
    }

    [Fact]
    public void APeak_CapsTheHeadroomSpent()
    {
        var range = new SceneRange(SceneWhite: 2f, Peak: 4f);

        Assert.Equal(4f, DrawExtended(2f, headroom: 8f, range), 0.05f);
        Assert.Equal(2f, DrawExtended(2f, headroom: 2f, range), 0.05f);
    }

    [Fact]
    public void WithoutASceneRange_TheWholeHeadroomIsAvailable()
    {
        // ACES puts 7.24 at white; the top of the curve stretches toward the display's ceiling.
        Assert.True(DrawExtended(50f, headroom: 8f, default) > 4.5f);
    }

    [Fact]
    public void Midtones_AreUntouchedByThePeak()
    {
        var capped = DrawExtended(0.18f, headroom: 8f, new SceneRange(SceneWhite: 7.24f, Peak: 4f));
        var open = DrawExtended(0.18f, headroom: 8f, default);

        Assert.Equal(open, capped, 0.01f);
    }

    private static byte DrawSdr(float light, ToneMapMode mode, SceneRange range)
    {
        using var image = LinearPixel(light);
        using var paint = HdrToneMapShader.CreatePaint(image, new SKSamplingOptions(SKFilterMode.Nearest), SKMatrix.CreateIdentity(), mode, exposureScale: 1f, whitePoint: 1f, SurfaceProfile.DisplayReferred(SKColorSpace.CreateSrgb()), range);
        Assert.NotNull(paint);

        var info = new SKImageInfo(1, 1, SKColorType.Rgba8888, SKAlphaType.Unpremul, SKColorSpace.CreateSrgb());
        using var surface = SKSurface.Create(info);
        surface.Canvas.DrawRect(new SKRect(0, 0, 1, 1), paint);

        using var snapshot = surface.Snapshot();
        using var readback = new SKBitmap(info);
        Assert.True(snapshot.ReadPixels(readback.Info, readback.GetPixels(), readback.RowBytes, 0, 0));

        return readback.GetPixel(0, 0).Red;
    }

    /// <summary>Draws into a linear extended-range surface, so the value read back is the light written.</summary>
    private static float DrawExtended(float light, float headroom, SceneRange range)
    {
        var linear = SKColorSpace.CreateSrgbLinear();

        using var image = LinearPixel(light);
        using var paint = HdrToneMapShader.CreatePaint(image, new SKSamplingOptions(SKFilterMode.Nearest), SKMatrix.CreateIdentity(), ToneMapMode.Aces, exposureScale: 1f, whitePoint: 1f, SurfaceProfile.Extended(linear, headroom), range);
        Assert.NotNull(paint);

        var info = new SKImageInfo(1, 1, SKColorType.RgbaF32, SKAlphaType.Unpremul, linear);
        using var surface = SKSurface.Create(info);
        surface.Canvas.DrawRect(new SKRect(0, 0, 1, 1), paint);

        using var snapshot = surface.Snapshot();
        using var readback = new SKBitmap(info);
        Assert.True(snapshot.ReadPixels(readback.Info, readback.GetPixels(), readback.RowBytes, 0, 0));

        return System.Runtime.InteropServices.MemoryMarshal.Cast<byte, float>(readback.GetPixelSpan())[1];
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