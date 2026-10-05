using System.Security.Cryptography;
using Lyra.Common;
using Lyra.Imaging.Content;
using Lyra.Imaging.Metadata;
using SkiaSharp;
using Lyra.Imaging.Decoding.Support;

namespace Lyra.Imaging.Decoding.Decoders;

internal class SkiaDecoder : DecoderBase, IThumbnailDecoder
{
    public override bool CanDecode(ImageFormatType format) => format
        is ImageFormatType.Bmp
        or ImageFormatType.Jfif
        or ImageFormatType.Jpeg;

    protected override void Decode(Composite composite, string path, CancellationToken ct)
    {
        using var stream = new MeasuredReadStream(DecoderIO.OpenSequentialRead(path), composite.ReportTransferred, composite.CompleteTransfer);

        if (TryDecodeAnimation(composite, stream, path, ct))
            return;

        stream.Position = 0;
        composite.ExifInfo = MetadataProcessor.ParseMetadata(stream, path);
        stream.Position = 0;

        // Skia takes the stream over, and closes it when it finds no image in it.
        var length = stream.Length;

        using var codec = SKCodec.Create(stream, out var opened)
                          ?? throw new LoadFailureException(LoadFailureKind.DecodeFailed, WhyNoCodec(opened, length));

        composite.ReportPixelCount(codec.Info.Width, codec.Info.Height);

        DecoderValidation.RequireSaneDimensions(codec.Info.Width, codec.Info.Height);
        DecoderValidation.RequireAvailableMemory(codec.Info.Width, codec.Info.Height);

        var srcColorSpace = codec.Info.ColorSpace ?? SKColorSpace.CreateSrgb();
        var info = new SKImageInfo(codec.Info.Width, codec.Info.Height, SKColorType.Rgba8888, SKAlphaType.Premul, srcColorSpace);
        var bitmap = new SKBitmap(info);

        try
        {
            // Ensure deterministic output if the image is truncated (IncompleteInput).
            bitmap.Erase(SKColors.Transparent);

            var result = codec.GetPixels(bitmap.Info, bitmap.GetPixels());

            if (result is SKCodecResult.InvalidInput or SKCodecResult.IncompleteInput
                && codec.EncodedFormat == SKEncodedImageFormat.Jpeg
                && TryRestoreEndMarker(stream, bitmap, ct))
            {
                result = SKCodecResult.Success;
                Logger.Warning(typeof(SkiaDecoder), $"Recovered a JPEG missing its end marker: {path}");
            }

            if (result != SKCodecResult.Success)
            {
                var damage = result switch
                {
                    SKCodecResult.IncompleteInput => "The file ends before the image does",
                    SKCodecResult.ErrorInInput    => "The image data is corrupt partway through",
                    _ => throw new LoadFailureException(LoadFailureKind.DecodeFailed, WhyNotDecoded(result))
                };

                Logger.Warning(typeof(SkiaDecoder), $"{damage}: {path}");
                composite.Warning = LoadWarning.PartiallyDecoded(damage);
            }

            ct.ThrowIfCancellationRequested();

            var upright = OrientationTransform.Apply(bitmap, codec.EncodedOrigin);

            if (!ReferenceEquals(upright, bitmap))
                composite.AppliedOrientation = (ExifOrientation)codec.EncodedOrigin;

            bitmap = upright;
        }
        catch
        {
            bitmap.Dispose();
            throw;
        }

        // The builder takes ownership of the bitmap and keeps it alive for the image it makes.
        composite.Content = RasterContentBuilder.Build(bitmap, composite);
    }
    
    protected virtual bool TryDecodeAnimation(Composite composite, Stream stream, string path, CancellationToken ct) => false;

    private static string WhyNoCodec(SKCodecResult result, long length) => result switch
    {
        _ when length == 0            => "The file is empty",
        SKCodecResult.Unimplemented   => "The file is not an image, or uses a variant of its format that cannot be decoded",
        SKCodecResult.IncompleteInput => "The file ends before the image header does",
        SKCodecResult.InvalidInput or SKCodecResult.ErrorInInput => "The image header is corrupt",
        _                             => $"The image could not be read ({result})"
    };

    private static string WhyNotDecoded(SKCodecResult result) => result switch
    {
        SKCodecResult.InvalidInput => "The image data is not valid",
        _                          => $"The image data could not be decoded ({result})"
    };

    private static readonly byte[] EndMarker = [0xFF, 0xD9];
    private static readonly byte[] FirstFiller = Filler(seed: 1);
    private static readonly byte[] SecondFiller = Filler(seed: 2);
    
    private static bool TryRestoreEndMarker(Stream stream, SKBitmap bitmap, CancellationToken ct)
    {
        if (stream.Length < 4 || EndsWithEoi(stream))
            return false;

        var data = DecoderIO.ReadToEnd(stream, ct);

        // JPEG SOI marker (FF D8).
        if (data[0] != 0xFF || data[1] != 0xD8)
            return false;

        using var scratch = TryAllocate(bitmap.Info);
        if (scratch is null)
            return false;

        if (DecodeHash(data, FirstFiller, scratch, ct) is not { } first || DecodeHash(data, SecondFiller, scratch, ct) is not { } second || !first.SequenceEqual(second))
            return false;

        if (Decode(data, [], scratch, ct) != SKCodecResult.Success)
            return false;

        unsafe
        {
            Buffer.MemoryCopy((void*)scratch.GetPixels(), (void*)bitmap.GetPixels(), bitmap.ByteCount, scratch.ByteCount);
        }

        return true;
    }

    private static byte[]? DecodeHash(byte[] data, byte[] filler, SKBitmap scratch, CancellationToken ct) =>
        Decode(data, filler, scratch, ct) == SKCodecResult.Success ? SHA256.HashData(scratch.GetPixelSpan()) : null;

    /// <summary>The data with <paramref name="filler"/> and an end marker after it, decoded into <paramref name="into"/>.</summary>
    private static SKCodecResult Decode(byte[] data, byte[] filler, SKBitmap into, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        using var memory = new MemoryStream([.. data, .. filler, .. EndMarker], writable: false);
        using var codec = SKCodec.Create(memory);
        if (codec is null)
            return SKCodecResult.InvalidInput;

        into.Erase(SKColors.Transparent);
        return codec.GetPixels(into.Info, into.GetPixels());
    }

    /// <summary>Bytes that decode as arbitrary image data, and never as a marker (no FF).</summary>
    private static byte[] Filler(int seed)
    {
        var random = new Random(seed);
        return [.. Enumerable.Range(0, 64).Select(_ => (byte)random.Next(0xFF))];
    }

    private static SKBitmap? TryAllocate(SKImageInfo info)
    {
        try
        {
            var bitmap = new SKBitmap(info);
            if (bitmap.GetPixels() != IntPtr.Zero)
                return bitmap;

            bitmap.Dispose();
            return null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static bool EndsWithEoi(Stream stream)
    {
        Span<byte> tail = stackalloc byte[2];
        stream.Position = stream.Length - 2;
        stream.ReadExactly(tail);

        return tail[0] == 0xFF && tail[1] == 0xD9;
    }

    public SKBitmap? DecodeThumbnail(string path, int maxDimension, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        using var stream = DecoderIO.OpenSequentialRead(path);
        using var codec = SKCodec.Create(stream);
        if (codec is null)
            return null;

        var (fullWidth, fullHeight) = (codec.Info.Width, codec.Info.Height);
        if (fullWidth <= 0 || fullHeight <= 0)
            return null;

        // Request a downscale; the codec snaps to the nearest scale it can do natively
        // (JPEG: DCT 1, 1/2, 1/4, 1/8). For codecs without native scaling this returns
        // the full size, and the final downscale to the hash grid happens in the caller.
        var desiredScale = Math.Min(1f, (float)maxDimension / Math.Max(fullWidth, fullHeight));
        var scaled = codec.GetScaledDimensions(desiredScale);

        DecoderValidation.RequireAvailableMemory(scaled.Width, scaled.Height);

        var info = new SKImageInfo(scaled.Width, scaled.Height, SKColorType.Rgba8888, SKAlphaType.Unpremul);
        var bitmap = new SKBitmap(info);
        bitmap.Erase(SKColors.Transparent); // deterministic output on truncated input

        var result = codec.GetPixels(info, bitmap.GetPixels());
        if (result is not (SKCodecResult.Success or SKCodecResult.IncompleteInput))
        {
            bitmap.Dispose();
            return null;
        }

        return OrientationTransform.Apply(bitmap, codec.EncodedOrigin);
    }
}