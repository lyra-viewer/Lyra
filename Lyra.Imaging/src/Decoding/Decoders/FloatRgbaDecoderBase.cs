using Lyra.Common;
using Lyra.Common.SystemExtensions;
using Lyra.Imaging.Content;
using SkiaSharp;
using static System.Threading.Thread;
using Lyra.Imaging.Decoding.Support;

namespace Lyra.Imaging.Decoding.Decoders;

internal abstract class FloatRgbaDecoderBase : IImageDecoder
{
    public abstract bool CanDecode(ImageFormatType format);

    /// <summary>
    /// Loads RGBA float pixels for <paramref name="path"/>. Implementations throw on failure;
    /// the returned buffer is owned by this base and released via its <c>Dispose</c>.
    /// </summary>
    protected abstract FloatImageBuffer LoadPixels(string path);

    public Task DecodeAsync(Composite composite, CancellationToken ct)
    {
        composite.DecoderName = GetType().Name;
        var path = composite.FileInfo.FullName;
        Logger.Debug($"[{GetType().Name}] [Thread: {CurrentThread.GetNameOrId()}] Decoding: {path}");

        ct.ThrowIfCancellationRequested();

        using var pixels = LoadPixels(path);

        ct.ThrowIfCancellationRequested();

        var width = pixels.Width;
        var height = pixels.Height;
        DecoderValidation.RequireSaneDimensions(GetType().Name, width, height, bytesPerPixel: sizeof(float) * 4);

        var info = new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Unpremul);
        var bitmap = new SKBitmap(info);

        unsafe
        {
            var floatSpan = pixels.AsSpan();
            var byteSpan = new Span<byte>((void*)bitmap.GetPixels(), checked(width * height * 4));

            ConvertPixels(floatSpan, byteSpan, width, height, ct, out var isGrayscale);

            composite.FormatSpecific["GrayScale"] = isGrayscale.ToString();
        }

        ct.ThrowIfCancellationRequested();

        bitmap.SetImmutable();
        var image = SKImage.FromBitmap(bitmap);

        composite.Content = new RasterContent(bitmap, image);

        return Task.CompletedTask;
    }

    private void ConvertPixels(Span<float> floatSpan, Span<byte> byteSpan, int width, int height, CancellationToken ct, out bool isGrayscale)
    {
        var totalPixels = width * height;

        // Grayscale detection stays sequential: it early-exits on the first pixel with a non-zero
        // green or blue channel, so for ordinary color images it costs next to nothing.
        var gray = true;
        for (var i = 0; i < totalPixels; i++)
        {
            if ((i & 0xFFFF) == 0)
                ct.ThrowIfCancellationRequested();

            if (floatSpan[i * 4 + 1] != 0f || floatSpan[i * 4 + 2] != 0f)
            {
                gray = false;
                break;
            }
        }

        isGrayscale = gray;

        // Tone-mapping is the per-pixel hotspot (an ACES curve + gamma encode per channel), so fan
        // it out across rows. Pin both buffers and hand the loop bodies raw pointers, since Span<T>
        // is a ref struct and cannot be captured by the parallel delegate.
        unsafe
        {
            fixed (float* srcPin = floatSpan)
            fixed (byte* dstPin = byteSpan)
            {
                var src = (nint)srcPin;
                var dst = (nint)dstPin;
                var broadcast = gray;
                var options = new ParallelOptions { CancellationToken = ct };

                Parallel.For(0, height, options, y => ConvertRow((float*)src, (byte*)dst, y, width, broadcast));
            }
        }
    }

    /// <summary>Tone-maps one row of pixels from float to 8-bit RGBA.</summary>
    private static unsafe void ConvertRow(float* src, byte* dst, int y, int width, bool broadcast)
    {
        var i = y * width;
        var end = i + width;

        for (; i < end; i++)
        {
            var idx = i * 4;
            var r = src[idx];
            var g = broadcast ? r : src[idx + 1];
            var b = broadcast ? r : src[idx + 2];
            var a = src[idx + 3];

            dst[idx + 0] = ToneMap(r);
            dst[idx + 1] = ToneMap(g);
            dst[idx + 2] = ToneMap(b);
            dst[idx + 3] = AlphaToByte(a);
        }
    }

    // Gamma 2.2 display encode as a lookup table over the tone-mapped [0,1] range.
    // 65536 entries keep the worst-case error at 1 byte level, even in deep shadows where
    // the gamma curve is steepest; the table is 64 KB and stays resident in L2.
    private const int GammaLutSize = 65536;
    private static readonly byte[] GammaLut = BuildGammaLut();

    private static byte[] BuildGammaLut()
    {
        var lut = new byte[GammaLutSize];
        for (var i = 0; i < GammaLutSize; i++)
        {
            var t = i / (float)(GammaLutSize - 1);
            lut[i] = (byte)Math.Clamp(MathF.Pow(t, 1f / 2.2f) * 255f, 0f, 255f);
        }

        return lut;
    }

    /// <summary>
    /// Maps a linear, scene-referred color channel to an 8-bit display value using the ACES
    /// filmic tone curve (Narkowicz 2015 approximation) followed by a gamma 2.2 display encode.
    /// The filmic curve rolls off highlights gracefully instead of clipping hard to white, which
    /// is what makes true HDR (EXR) content readable. NaN maps to 0 (black).
    /// </summary>
    private static byte ToneMap(float value)
    {
        if (float.IsNaN(value))
            return 0;

        var x = MathF.Max(value, 0f);

        // ACES filmic curve: (x * (a*x + b)) / (x * (c*x + d) + e).
        const float a = 2.51f;
        const float b = 0.03f;
        const float c = 2.43f;
        const float d = 0.59f;
        const float e = 0.14f;
        var toneMapped = Math.Clamp((x * ((a * x) + b)) / ((x * ((c * x) + d)) + e), 0f, 1f);
        
        return GammaLut[(int)((toneMapped * (GammaLutSize - 1)) + 0.5f)];
    }

    /// <summary>
    /// Converts a linear alpha (coverage) value to 8 bits. Unlike the color channels, alpha must
    /// NOT be gamma-encoded. NaN is treated as fully opaque so a bad alpha value never silently
    /// erases an otherwise valid pixel.
    /// </summary>
    private static byte AlphaToByte(float value)
    {
        if (float.IsNaN(value))
            return 255;

        return (byte)Math.Clamp(value * 255f, 0f, 255f);
    }
}