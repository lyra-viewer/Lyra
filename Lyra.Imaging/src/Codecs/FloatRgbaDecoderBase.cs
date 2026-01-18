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

    public async Task DecodeAsync(Composite composite, CancellationToken ct)
    {
        composite.DecoderName = GetType().Name;
        var path = composite.FileInfo.FullName;
        Logger.Debug($"[{GetType().Name}] [Thread: {CurrentThread.GetNameOrId()}] Decoding: {path}");

        ct.ThrowIfCancellationRequested();
        await Task.Run(() =>
        {
            var success = LoadPixels(path, out var ptr, out var width, out var height);
            if (!success || ptr == IntPtr.Zero)
            {
                Logger.Warning($"[{GetType().Name}] Failed to load native pixels or got null pointer for: {path}");
                return composite;
            }

            try
            {
                var totalPixels = width * height;
                var floatCount = totalPixels * 4;
                var info = new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Unpremul);
                using var bitmap = new SKBitmap(info);

                unsafe
                {
                    var floatSpan = new Span<float>((void*)ptr, floatCount);
                    var byteSpan = new Span<byte>((void*)bitmap.GetPixels(), width * height * 4);

                    ConvertPixels(floatSpan, byteSpan, width, height, out composite.IsGrayscale);
                }

                composite.Content = new RasterContent(SKImage.FromBitmap(bitmap));
                return composite;
            }
            finally
            {
                if (ptr != IntPtr.Zero)
                {
                    FreePixels(ptr);
                }
            }
        });
    }

    private void ConvertPixels(Span<float> floatSpan, Span<byte> byteSpan, int width, int height, out bool isGrayscale)
    {
        var totalPixels = width * height;

        isGrayscale = true;
        for (var i = 0; i < totalPixels && isGrayscale; i++)
        {
            if (floatSpan[i * 4 + 1] != 0f || floatSpan[i * 4 + 2] != 0f)
                isGrayscale = false;
        }

        for (var i = 0; i < totalPixels; i++)
        {
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