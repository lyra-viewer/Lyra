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
    protected abstract bool LoadPixels(string path, out IntPtr ptr, out int width, out int height);
    protected abstract void FreePixels(IntPtr ptr);

    public Task DecodeAsync(Composite composite, CancellationToken ct)
    {
        composite.DecoderName = GetType().Name;
        var path = composite.FileInfo.FullName;
        Logger.Debug($"[{GetType().Name}] [Thread: {CurrentThread.GetNameOrId()}] Decoding: {path}");

        ct.ThrowIfCancellationRequested();

        var success = LoadPixels(path, out var ptr, out var width, out var height);
        if (!success || ptr == IntPtr.Zero)
            throw new InvalidOperationException($"[{GetType().Name}] Failed to load native pixels or got null pointer for: {path}");

        try
        {
            ct.ThrowIfCancellationRequested();

            DecoderValidation.RequireSaneDimensions(GetType().Name, width, height, bytesPerPixel: sizeof(float) * 4);

            var totalPixels = checked(width * height);
            var floatCount = checked(totalPixels * 4);

            var info = new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Unpremul);
            var bitmap = new SKBitmap(info);

            unsafe
            {
                var floatSpan = new Span<float>((void*)ptr, floatCount);
                var byteSpan = new Span<byte>((void*)bitmap.GetPixels(), checked(width * height * 4));

                ConvertPixels(floatSpan, byteSpan, width, height, ct, out var isGrayscale);
                
                composite.FormatSpecific["GrayScale"] = isGrayscale.ToString();
            }

            ct.ThrowIfCancellationRequested();

            bitmap.SetImmutable();
            var image = SKImage.FromBitmap(bitmap);

            composite.Content = new RasterContent(bitmap, image);
        }
        finally
        {
            FreePixels(ptr);
        }

        return Task.CompletedTask;
    }

    private void ConvertPixels(Span<float> floatSpan, Span<byte> byteSpan, int width, int height, CancellationToken ct, out bool isGrayscale)
    {
        var totalPixels = width * height;

        isGrayscale = true;
        for (var i = 0; i < totalPixels && isGrayscale; i++)
        {
            if ((i & 0xFFFF) == 0)
                ct.ThrowIfCancellationRequested();

            if (floatSpan[i * 4 + 1] != 0f || floatSpan[i * 4 + 2] != 0f)
                isGrayscale = false;
        }

        for (var i = 0; i < totalPixels; i++)
        {
            if ((i & 0xFFFF) == 0)
                ct.ThrowIfCancellationRequested();

            var r = floatSpan[i * 4 + 0];
            var g = isGrayscale ? r : floatSpan[i * 4 + 1];
            var b = isGrayscale ? r : floatSpan[i * 4 + 2];
            var a = floatSpan[i * 4 + 3];

            var idx = i * 4;
            byteSpan[idx + 0] = ToneMap(r);
            byteSpan[idx + 1] = ToneMap(g);
            byteSpan[idx + 2] = ToneMap(b);
            byteSpan[idx + 3] = AlphaToByte(a);
        }
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

        var encoded = MathF.Pow(toneMapped, 1f / 2.2f) * 255f;
        return (byte)Math.Clamp(encoded, 0f, 255f);
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