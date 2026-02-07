using Lyra.Common;
using Lyra.Common.SystemExtensions;
using Lyra.Imaging.Content;
using SkiaSharp;
using static System.Threading.Thread;

namespace Lyra.Imaging.Codecs;

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

            var totalPixels = checked(width * height);
            var floatCount = checked(totalPixels * 4);

            var info = new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Unpremul);
            var bitmap = new SKBitmap(info);

            unsafe
            {
                var floatSpan = new Span<float>((void*)ptr, floatCount);
                var byteSpan = new Span<byte>((void*)bitmap.GetPixels(), checked(width * height * 4));

                ConvertPixels(floatSpan, byteSpan, width, height, ct, out composite.IsGrayscale);
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
            byteSpan[idx + 3] = ToneMap(a);
        }
    }

    private static byte ToneMap(float value)
    {
        value = MathF.Pow(MathF.Max(value, 0), 1f / 2.2f) * 255f;
        return (byte)Math.Clamp(value, 0, 255);
    }
}