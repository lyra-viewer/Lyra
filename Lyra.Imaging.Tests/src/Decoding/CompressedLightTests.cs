using Lyra.Imaging.Content;
using Lyra.Imaging.Content.Hdr;
using Lyra.Imaging.Decoding.Support;
using SkiaSharp;
using Xunit;

namespace Lyra.Imaging.Tests.Decoding;

/// <summary>
/// Images too large for half-float tiles keep them as compressed light, which the renderer expands
/// and tone-maps live. Before this they were tone-mapped at decode and ignored the controls.
/// </summary>
public class CompressedLightTests
{
    [Theory]
    [InlineData(0.05f, 0.08f)]
    [InlineData(0.18f, 0.03f)]
    [InlineData(1f, 0.03f)]
    [InlineData(4f, 0.06f)]
    [InlineData(30f, 0.4f)]
    public void LightSurvivesTheRoundTrip(float light, float tolerance)
    {
        var back = CompressedLight.Decode(CompressedLight.Encode(light));

        Assert.InRange(back, light * (1f - tolerance), light * (1f + tolerance));
    }

    [Fact]
    public void WhatIsNotLightStoresAsBlack()
    {
        Assert.Equal(0, CompressedLight.Encode(float.NaN));
        Assert.Equal(0, CompressedLight.Encode(-3f));
        Assert.Equal(0f, CompressedLight.Decode(0));
    }

    [Fact]
    public void ASunStoresAtTheCeiling()
    {
        Assert.Equal(255, CompressedLight.Encode(float.PositiveInfinity));
        Assert.Equal(255, CompressedLight.Encode(1e9f));
        Assert.Equal(CompressedLight.Ceiling, CompressedLight.Decode(255), 0.5f);
    }

    [Fact]
    public void BrighterLightNeverStoresDarker()
    {
        var previous = 0;
        for (var light = 0.001f; light < 2000f; light *= 1.01f)
        {
            var stored = CompressedLight.Encode(light);
            Assert.True(stored >= previous, $"{light} stored as {stored}, below {previous}.");
            previous = stored;
        }
    }

    [Fact]
    public void OverTheSceneBudget_TheImageStaysLive()
    {
        float[] light = [0.01f, 0.18f, 1f, 4f, 30f, float.PositiveInfinity, float.NaN, 0.5f];
        var floats = new float[light.Length * 4];
        for (var i = 0; i < light.Length; i++)
        {
            floats[i * 4] = light[i];
            floats[i * 4 + 1] = light[i];
            floats[i * 4 + 2] = light[i] * 0.5f;
            floats[i * 4 + 3] = 1f;
        }

        var composite = new Composite(new FileInfo("huge.exr"));
        ICompositeContent content;

        unsafe
        {
            fixed (float* p = floats)
                content = HdrImageBuilder.Build(new FloatRgbaRows(p, light.Length, 1), composite, CancellationToken.None, out _, default, new HdrBudgets(LivePixels: 1, TiledSceneBytes: 1));
        }

        using (content)
        {
            composite.Content = content;

            var hdr = Assert.IsType<HdrRasterContent>(content);
            Assert.Equal(SceneEncoding.Compressed, hdr.Encoding);
            Assert.Equal(SKColorType.Rgba8888, hdr.Image.ColorType);
            Assert.Null(composite.HdrBakedReason);
            Assert.True(composite.IsHdrDecoded);

            using var bitmap = SKBitmap.FromImage(hdr.Image);
            for (var i = 0; i < light.Length; i++)
            {
                var pixel = bitmap.GetPixel(i, 0);

                Assert.InRange(pixel.Red - CompressedLight.Encode(light[i]), -1, 1);
                Assert.InRange(pixel.Blue - CompressedLight.Encode(light[i] * 0.5f), -1, 1);
                Assert.Equal(255, pixel.Alpha);
            }
        }
    }
}
