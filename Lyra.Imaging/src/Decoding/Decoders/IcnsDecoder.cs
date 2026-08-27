using Lyra.Common;
using Lyra.Common.SystemExtensions;
using Lyra.Imaging.Content;
using Lyra.Imaging.Decoding.Support;
using Lyra.Imaging.Interop;
using Lyra.ManagedCodecs.Raster;
using Lyra.ManagedCodecs.Raster.Icns;
using SkiaSharp;
using static System.Threading.Thread;

namespace Lyra.Imaging.Decoding.Decoders;

internal sealed class IcnsDecoder : IImageDecoder
{
    public bool CanDecode(ImageFormatType format) => format == ImageFormatType.Icns;

    public Task DecodeAsync(Composite composite, CancellationToken ct)
    {
        var path = composite.FileInfo.FullName;
        composite.DecoderName = GetType().Name;
        Logger.Debug($"[IcnsDecoder] [Thread: {CurrentThread.GetNameOrId()}] Decoding: {path}");

        var data = File.ReadAllBytes(path);

        ct.ThrowIfCancellationRequested();

        var entries = IcnsReader.ReadEntries(data);

        // Not every .icns is a container. macOS itself ships plain PNGs under the extension
        // (GameControllerMacSettings.appex), and routing is by extension, so this decoder is
        // where they land. Showing the image beats refusing the file.
        if (entries.Count == 0)
            return DecodeAsPlainImage(composite, data, path);

        var icons = new List<DecodedIcon>(entries.Count);

        foreach (var entry in entries)
        {
            ct.ThrowIfCancellationRequested();

            SKBitmap? bitmap = null;
            try
            {
                bitmap = DecodeEntry(data, entry);
            }
            catch (Exception ex)
            {
                Logger.Warning($"[IcnsDecoder] Entry '{entry.Type.Code}' failed to decode: {ex.Message}");
            }

            if (bitmap is null)
            {
                Logger.Warning($"[IcnsDecoder] Skipping unreadable entry '{entry.Type.Code}' in {path}.");
                continue;
            }

            var encoding = EncodingName(data, entry);

            bitmap.SetImmutable();

            icons.Add(new DecodedIcon(
                entry,
                encoding,
                new RasterContent(bitmap, SKImage.FromBitmap(bitmap)),
                BuildVariant(entry, bitmap, encoding))
            );
        }

        if (icons.Count == 0)
            throw new InvalidOperationException($"[IcnsDecoder] Every icon failed to decode in: {path}");
        
        var ordered = icons
            .OrderByDescending(icon => (long)icon.Variant.Width * icon.Variant.Height)
            .ThenByDescending(icon => icon.Entry.Scale)
            .ThenBy(icon => icon.Entry.Type.Code, StringComparer.Ordinal)
            .ToList();

        composite.Content = new VariantRasterContent(
            [.. ordered.Select(icon => icon.Variant)],
            [.. ordered.Select(icon => icon.Content)],
            active: 0
        );

        Describe(composite, data, ordered);

        return Task.CompletedTask;
    }

    /// <summary>One icon that decoded, kept whole so the four parallel lists cannot drift apart.</summary>
    private sealed record DecodedIcon(IcnsEntry Entry, string Encoding, ICompositeContent Content, ImageVariant Variant);

    private static void Describe(Composite composite, byte[] data, List<DecodedIcon> icons)
    {
        composite.AddFormatSpecific("Icons", icons.Count.ToString());
        composite.AddFormatSpecific("Size Range", icons.Count == 1 ? icons[0].Variant.Label : $"{icons[^1].Variant.Label} to {icons[0].Variant.Label}");

        var retina = icons.Count(icon => icon.Entry.Scale > 1);
        if (retina > 0)
            composite.AddFormatSpecific("Retina Entries", $"{retina} of {icons.Count}");

        // "PNG x6, ARGB x2" - ordered by count so the dominant encoding reads first.
        var encodings = icons
            .GroupBy(icon => icon.Encoding, StringComparer.Ordinal)
            .OrderByDescending(g => g.Count())
            .ThenBy(g => g.Key, StringComparer.Ordinal)
            .Select(g => g.Count() == 1 ? g.Key : $"{g.Key} x{g.Count()}");

        composite.AddFormatSpecific("Encodings", string.Join(", ", encodings));
        composite.AddFormatSpecific("Types", string.Join(", ", icons.Select(icon => icon.Entry.Type.Code).Order(StringComparer.Ordinal)));

        var iconBytes = icons.Sum(icon => icon.Variant.ByteSize);
        composite.AddFormatSpecific("Icon Data", $"{Formatters.SizeToStr(iconBytes)} of {Formatters.SizeToStr(data.Length)}");
    }

    private static Task DecodeAsPlainImage(Composite composite, byte[] data, string path)
    {
        using var skData = SKData.CreateCopy(data);
        var bitmap = SKBitmap.Decode(skData)
                     ?? throw new InvalidOperationException($"[IcnsDecoder] No readable icons in: {path}");

        Logger.Debug($"[IcnsDecoder] {Path.GetFileName(path)} is not a container; decoded as a plain image.");

        bitmap.SetImmutable();
        composite.Content = new RasterContent(bitmap, SKImage.FromBitmap(bitmap));

        return Task.CompletedTask;
    }
    
    private static ImageVariant BuildVariant(IcnsEntry entry, SKBitmap bitmap, string encoding)
    {
        var label = $"{bitmap.Width} x {bitmap.Height}";
        if (entry.Scale > 1)
            label += $" @{entry.Scale}x";

        return new ImageVariant(label, bitmap.Width, bitmap.Height, $"{entry.Type.Code} - {encoding}", entry.PayloadLength);
    }
    
    private static string EncodingName(byte[] data, IcnsEntry entry)
    {
        if (entry.Kind != IcnsPayloadKind.Embedded)
            return entry.Kind == IcnsPayloadKind.Argb ? "ARGB" : "RLE24";

        var payload = entry.Payload(data);

        if (IsJpeg2000(payload))
            return "JPEG 2000";

        if (payload.Length >= 4 && payload[0] == 0x89 && payload[1] == (byte)'P' && payload[2] == (byte)'N' && payload[3] == (byte)'G')
            return "PNG";

        if (payload.Length >= 3 && payload[0] == 0xFF && payload[1] == 0xD8 && payload[2] == 0xFF)
            return "JPEG";

        return "Unknown";
    }

    private static SKBitmap? DecodeEntry(byte[] data, IcnsEntry entry)
    {
        if (entry.Kind == IcnsPayloadKind.Embedded)
            return DecodeEmbedded(data, entry);

        var decoded = IcnsReader.Decode(data, entry);
        return decoded is { } image ? FromDecodedImage(image) : null;
    }
    
    private static SKBitmap? DecodeEmbedded(byte[] data, IcnsEntry entry)
    {
        var payload = entry.Payload(data);

        if (IsJpeg2000(payload))
            return DecodeJpeg2000(data, entry);

        using var skData = SKData.CreateCopy(payload.ToArray());
        return SKBitmap.Decode(skData);
    }

    private static bool IsJpeg2000(ReadOnlySpan<byte> payload) =>
        (payload.Length >= 6 && payload[0] == 0x00 && payload[1] == 0x00 && payload[2] == 0x00 && payload[3] == 0x0C && payload[4] == (byte)'j' && payload[5] == (byte)'P')
        || (payload.Length >= 4 && payload[0] == 0xFF && payload[1] == 0x4F && payload[2] == 0xFF && payload[3] == 0x51);

    private static unsafe SKBitmap? DecodeJpeg2000(byte[] data, IcnsEntry entry)
    {
        var nativePixels = IntPtr.Zero;

        try
        {
            fixed (byte* payload = &data[entry.PayloadOffset])
            {
                var ok = J2KNative.decode_j2k_rgba8_from_memory(
                    (IntPtr)payload,
                    (nuint)entry.PayloadLength,
                    0,
                    out nativePixels,
                    out var width,
                    out var height,
                    out var strideBytes,
                    out _,
                    out _
                );

                if (!ok || nativePixels == IntPtr.Zero)
                {
                    var error = NativeErrors.GetUtf8ZOrAnsiZ(J2KNative.get_last_j2k_error());
                    Logger.Warning($"[IcnsDecoder] JPEG 2000 entry '{entry.Type.Code}' failed: {error}");
                    return null;
                }

                DecoderValidation.RequireSaneDimensions("IcnsDecoder", width, height);
                DecoderValidation.RequireValidStride("IcnsDecoder", strideBytes, width);

                var info = new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Unpremul);
                var bitmap = new SKBitmap(info);

                var src = (byte*)nativePixels;
                var dst = (byte*)bitmap.GetPixels();
                var dstStride = bitmap.Info.RowBytes;
                var rowBytes = Math.Min(strideBytes, dstStride);

                for (var y = 0; y < height; y++)
                    Buffer.MemoryCopy(src + ((nint)y * strideBytes), dst + ((nint)y * dstStride), dstStride, rowBytes);

                return bitmap;
            }
        }
        finally
        {
            if (nativePixels != IntPtr.Zero)
                J2KNative.free_j2k_pixels(nativePixels);
        }
    }

    /// <summary>
    /// Copies row by row rather than in one block: SKBitmap is free to pad its rows, and a
    /// straight copy would shear the image whenever RowBytes exceeds width * 4.
    /// </summary>
    private static unsafe SKBitmap FromDecodedImage(DecodedImage image)
    {
        var info = new SKImageInfo(image.Width, image.Height, SKColorType.Rgba8888, SKAlphaType.Unpremul);
        var bitmap = new SKBitmap(info);

        var dst = (byte*)bitmap.GetPixels();
        var dstStride = bitmap.Info.RowBytes;
        var srcStride = image.Width * 4;

        fixed (byte* src = image.Pixels)
        {
            for (var y = 0; y < image.Height; y++)
                Buffer.MemoryCopy(src + ((nint)y * srcStride), dst + ((nint)y * dstStride), dstStride, srcStride);
        }

        return bitmap;
    }
}
