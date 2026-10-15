using Lyra.Imaging.Content;
using Lyra.Imaging.Decoding.Support;
using SkiaSharp;
using Xunit;

namespace Lyra.Imaging.Tests.Decoding;

/// <summary>
/// 16-bit RGB reaches the HDR paths through <see cref="Rgb16Rows"/> rather than as float RGBA, so
/// every path must come out exactly as it does for the same light handed over as floats.
/// </summary>
public class LinearRowsTests
{
    private const int Width = 37;
    private const int Height = 23;

    [Fact]
    public void Rgb16_BuildsTheSameHalfFloatImageAsFloats()
    {
        var (rgb16, floats) = Gradient();

        using var fromFloats = Build(floats, out var floatFacts);
        using var fromRgb16 = BuildRgb16(rgb16, out var rgb16Facts);

        var a = Assert.IsType<HdrRasterContent>(fromFloats);
        var b = Assert.IsType<HdrRasterContent>(fromRgb16);

        Assert.Equal(a.WhitePoint, b.WhitePoint);
        Assert.Equal(floatFacts, rgb16Facts);
        Assert.True(Pixels(a.Image).SequenceEqual(Pixels(b.Image)), "the half-float pixels differ");
    }

    [Fact]
    public void Rgb16_ToneMapsTheSameAsFloats()
    {
        var (rgb16, floats) = Gradient();

        using var fromFloats = new SKBitmap(new SKImageInfo(Width, Height, SKColorType.Rgba8888, SKAlphaType.Unpremul));
        using var fromRgb16 = new SKBitmap(fromFloats.Info);

        HdrToneMap.ToBitmap(floats, fromFloats, 1.5f, CancellationToken.None, out var floatGray);

        unsafe
        {
            fixed (ushort* p = rgb16)
                HdrToneMap.ToBitmap(new Rgb16Rows(p, Width, Height), fromRgb16, 1.5f, default, CancellationToken.None, out _);
        }

        Assert.False(floatGray);
        Assert.True(fromFloats.GetPixelSpan().SequenceEqual(fromRgb16.GetPixelSpan()), "the tone-mapped pixels differ");
    }

    [Fact]
    public void Rgb16_MeasuresTheSameWhitePointAsFloats()
    {
        var (rgb16, floats) = Gradient();

        unsafe
        {
            fixed (ushort* p = rgb16)
                Assert.Equal(HdrToneMap.MeasureWhitePoint(floats), HdrToneMap.MeasureWhitePoint(new Rgb16Rows(p, Width, Height)));
        }
    }

    [Fact]
    public void Rgb16_DownsamplesTheSameAsFloats()
    {
        var (rgb16, floats) = Gradient();

        SKImage? fromFloats, fromRgb16;

        unsafe
        {
            fixed (float* f = floats)
                fromFloats = HdrImageBuilder.DownsampleToHalfFloat(new FloatRgbaRows(f, Width, Height), 9, 5, CancellationToken.None);

            fixed (ushort* p = rgb16)
                fromRgb16 = HdrImageBuilder.DownsampleToHalfFloat(new Rgb16Rows(p, Width, Height), 9, 5, CancellationToken.None);
        }

        Assert.NotNull(fromFloats);
        Assert.NotNull(fromRgb16);

        using (fromFloats)
        using (fromRgb16)
            Assert.True(Pixels(fromFloats).SequenceEqual(Pixels(fromRgb16)), "the downsampled pixels differ");
    }

    [Fact]
    public void Rgb16_IsOpaque()
    {
        var rgb16 = new ushort[2 * 3];

        unsafe
        {
            fixed (ushort* p = rgb16)
            {
                var scratch = stackalloc float[2 * 4];
                var row = new Rgb16Rows(p, 2, 1).Row(0, scratch);

                Assert.Equal(1f, row[3]);
                Assert.Equal(1f, row[7]);
            }
        }
    }

    [Fact]
    public void Downsampling_AveragesEachBox()
    {
        // 4x2 into 2x1: each output pixel is the mean of a 2x2 box.
        float[] floats =
        [
            0.0f, 0, 0, 1,   0.4f, 0, 0, 1,   1.0f, 0, 0, 1,   1.0f, 0, 0, 1,
            0.2f, 0, 0, 1,   0.2f, 0, 0, 1,   1.0f, 0, 0, 1,   3.0f, 0, 0, 1
        ];

        SKImage? image;

        unsafe
        {
            fixed (float* p = floats)
                image = HdrImageBuilder.DownsampleToHalfFloat(new FloatRgbaRows(p, 4, 2), 2, 1, CancellationToken.None);
        }

        Assert.NotNull(image);
        using (image)
        {
            var pixels = Pixels(image);
            Assert.Equal(0.2f, (float)pixels[0], 0.002f);
            Assert.Equal(1.5f, (float)pixels[4], 0.002f);
            Assert.Equal(1f, (float)pixels[3]);
        }
    }

    [Fact]
    public void AnImageClippedAtWhite_IsWithinSdrWhite()
    {
        var floats = new float[Width * Height * 4];
        Array.Fill(floats, 1f);

        using var content = Build(floats, out var facts);

        Assert.Equal("Within SDR white", facts.DynamicRange);
    }

    /// <summary>The same light twice: 16-bit, and the floats it stands for.</summary>
    private static (ushort[] Rgb16, float[] Floats) Gradient()
    {
        var rgb16 = new ushort[Width * Height * 3];
        var floats = new float[Width * Height * 4];

        for (var i = 0; i < Width * Height; i++)
        {
            for (var c = 0; c < 3; c++)
            {
                var value = (ushort)((i * 977 + c * 12_345) % 65536);
                rgb16[i * 3 + c] = value;
                floats[i * 4 + c] = value * (1f / 65535f);
            }

            floats[i * 4 + 3] = 1f;
        }

        return (rgb16, floats);
    }

    private static ICompositeContent Build(float[] floats, out PixelFacts facts) =>
        HdrImageBuilder.Build(floats, Width, Height, new Composite(new FileInfo("floats.exr")), CancellationToken.None, out facts);

    private static unsafe ICompositeContent BuildRgb16(ushort[] rgb16, out PixelFacts facts)
    {
        fixed (ushort* p = rgb16)
            return HdrImageBuilder.Build(new Rgb16Rows(p, Width, Height), new Composite(new FileInfo("rgb16.dng")), CancellationToken.None, out facts);
    }

    private static Half[] Pixels(SKImage image)
    {
        var info = new SKImageInfo(image.Width, image.Height, SKColorType.RgbaF16, SKAlphaType.Unpremul, SKColorSpace.CreateSrgbLinear());
        var pixels = new Half[image.Width * image.Height * 4];

        unsafe
        {
            fixed (Half* p = pixels)
                Assert.True(image.ReadPixels(info, (IntPtr)p, info.RowBytes, 0, 0));
        }

        return pixels;
    }
}