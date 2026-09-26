using Lyra.Common;
using Lyra.Imaging.Content;
using Lyra.Imaging.Decoding.Support;
using Lyra.Imaging.Interop;
using Lyra.Imaging.Loading;
using SkiaSharp;

namespace Lyra.Imaging.Decoding.Decoders.Tiff;

/// <summary>
/// Reads a directory at its own sample layout: the layouts libtiff's RGBA interface refuses, such
/// as float samples or more than four channels. Held whole - nothing streams these.
/// </summary>
internal static class TiffNativeLayout
{
    /// <summary>The most one image may cost while it is read at its own sample layout.</summary>
    internal static long BudgetBytes => ImageLoader.CacheBudgetBytes / 2;

    /// <summary>
    /// What reading this directory at its own layout costs at peak: the file's own packing plus
    /// the converted output, which overlap.
    /// </summary>
    internal static long PeakBytes(TiffNative.DirectoryInfo info, bool isFloat)
    {
        var pixels = (long)info.Width * info.Height;
        var packed = (pixels * info.BitsPerSample * info.SamplesPerPixel + 7) / 8;
        var converted = pixels * (isFloat ? 16 : info.SamplesPerPixel == 1 ? 1 : 4);

        return packed + converted;
    }

    /// <summary>
    /// Refuses a directory whose own layout cannot be read within the budget, before anything is
    /// allocated for it.
    /// </summary>
    internal static void RequireWithinBudget(string path, TiffNative.DirectoryInfo info, bool isFloat)
    {
        if (info.Width == 0 || info.Height == 0 || info.Width > int.MaxValue || info.Height > int.MaxValue)
            throw new InvalidOperationException($"[TiffDecoder] {Path.GetFileName(path)} declares dimensions this path cannot read: {info.Width}x{info.Height}.");

        DecoderValidation.RequireSaneDimensions(nameof(TiffDecoder), (int)info.Width, (int)info.Height);

        var peak = PeakBytes(info, isFloat);
        var budget = BudgetBytes;

        if (peak <= budget)
            return;

        throw new InvalidOperationException($"[TiffDecoder] {Path.GetFileName(path)} is {info.Width}x{info.Height} at {info.BitsPerSample}-bit " +
                                            $"x{info.SamplesPerPixel}, which the RGBA interface refuses and which reading at its own layout would " +
                                            $"cost {peak / (1024 * 1024)} MB at peak, over the {budget / (1024 * 1024)} MB this machine allows. " +
                                            "Layouts read this way are held whole; nothing streams them.");
    }

    public static ICompositeContent Decode(string path, int directory, TiffNative.DirectoryInfo info, Composite composite, CancellationToken ct)
    {
        var transfer = new IoTally(composite);
        var (read, kind) = Read(path, directory, info, ct, transfer);

        transfer.ReportTo(composite);

        try
        {
            ct.ThrowIfCancellationRequested();

            DecoderValidation.RequireSaneDimensions(nameof(TiffDecoder), read.Width, read.Height);

            if (kind == TiffNative.OutputKind.RgbaFloat)
            {
                unsafe
                {
                    var floats = new Span<float>((void*)read.Pixels, checked(read.Width * read.Height * 4));
                    var content = HdrImageBuilder.Build(floats, read.Width, read.Height, composite, ct, out var isGrayscale);

                    composite.AddFormatSpecific("GrayScale", isGrayscale.ToString());
                    return content;
                }
            }

            return RasterContentBuilder.Build(IntegerBitmap(read, kind), composite);
        }
        finally
        {
            TiffNative.free_tiff_pixels(read.Pixels);
        }
    }
    
    public static SKBitmap? Thumbnail(string path, int directory, TiffNative.DirectoryInfo info, int maxDimension, CancellationToken ct)
    {
        var (read, kind) = Read(path, directory, info, ct, tally: null);

        try
        {
            ct.ThrowIfCancellationRequested();

            DecoderValidation.RequireSaneDimensions(nameof(TiffDecoder), read.Width, read.Height);

            var bitmap = kind == TiffNative.OutputKind.RgbaFloat
                ? ClampedFloatBitmap(read)
                : IntegerBitmap(read, kind);

            return ThumbnailScaler.ResizeToThumbnail(bitmap, maxDimension);
        }
        finally
        {
            TiffNative.free_tiff_pixels(read.Pixels);
        }
    }

    private static (TiffRead Read, TiffNative.OutputKind Kind) Read(string path, int directory, TiffNative.DirectoryInfo info, CancellationToken ct, IoTally? tally)
    {
        var isFloat = info.SampleFormat == 3;

        var kind = isFloat
            ? TiffNative.OutputKind.RgbaFloat
            : info.SamplesPerPixel == 1
                ? TiffNative.OutputKind.Gray8
                : TiffNative.OutputKind.Rgba8;

        RequireWithinBudget(path, info, isFloat);

        Logger.Info($"[TiffDecoder] {Path.GetFileName(path)} is {info.BitsPerSample}-bit x{info.SamplesPerPixel} " +
                    $"{(isFloat ? "float" : "integer")}, which the RGBA interface refuses; " +
                    $"reading it at its own layout as {kind} " +
                    $"({PeakBytes(info, isFloat) / (1024 * 1024)} MB at peak).");

        ct.ThrowIfCancellationRequested();

        var read = TiffReads.NativeLayout(path, directory, kind, PeakBytes(info, isFloat), ct, tally);

        if (!read.HasPixels)
            throw new InvalidOperationException($"[TiffDecoder] Failed to decode {path} at its own layout. {read.Reason}");

        return (read, kind);
    }

    private static SKBitmap IntegerBitmap(TiffRead read, TiffNative.OutputKind kind)
    {
        var gray = kind == TiffNative.OutputKind.Gray8;

        var bitmap = new SKBitmap(new SKImageInfo(read.Width, read.Height,
            gray ? SKColorType.Gray8 : SKColorType.Rgba8888,
            gray ? SKAlphaType.Opaque : SKAlphaType.Unpremul));

        if (gray)
        {
            PixelCopy.CopyRows(read.Pixels, read.Stride, bitmap);
        }
        else
        {
            unsafe
            {
                PixelCopy.CopyTightRgba(new ReadOnlySpan<byte>((void*)read.Pixels, checked(read.Width * read.Height * 4)), bitmap);
            }
        }

        return bitmap;
    }

    private static unsafe SKBitmap ClampedFloatBitmap(TiffRead read)
    {
        var bitmap = new SKBitmap(new SKImageInfo(read.Width, read.Height, SKColorType.Rgba8888, SKAlphaType.Unpremul));

        var source = new ReadOnlySpan<float>((void*)read.Pixels, checked(read.Width * read.Height * 4));
        var target = (byte*)bitmap.GetPixels();

        for (var y = 0; y < read.Height; y++)
        {
            var row = target + (long)y * bitmap.RowBytes;
            var from = source.Slice(y * read.Width * 4, read.Width * 4);

            for (var i = 0; i < from.Length; i++)
                row[i] = (byte)(Math.Clamp(from[i], 0f, 1f) * 255f + 0.5f);
        }

        return bitmap;
    }
}
