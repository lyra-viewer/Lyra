using Lyra.Common;
using Lyra.Common.SystemExtensions;
using Lyra.Imaging.Content;
using Lyra.Imaging.Metadata;
using Lyra.ManagedCodecs.Raster;
using Lyra.ManagedCodecs.Raster.Ico;
using SkiaSharp;
using static System.Threading.Thread;

namespace Lyra.Imaging.Decoding.Decoders;

internal sealed class IcoDecoder : IImageDecoder, IThumbnailDecoder
{
    public bool CanDecode(ImageFormatType format) => format == ImageFormatType.Ico;

    public Task DecodeAsync(Composite composite, CancellationToken ct)
    {
        var path = composite.FileInfo.FullName;
        composite.DecoderName = GetType().Name;
        Logger.Debug($"[IcoDecoder] [Thread: {CurrentThread.GetNameOrId()}] Decoding: {path}");

        composite.ExifInfo = MetadataProcessor.ParseMetadata(path);

        var data = File.ReadAllBytes(path);

        ct.ThrowIfCancellationRequested();

        var entries = IcoReader.ReadEntries(data);

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
                Logger.Warning($"[IcoDecoder] Entry {Describe(entry)} failed to decode: {ex.Message}");
            }

            if (bitmap is null)
            {
                Logger.Warning($"[IcoDecoder] Skipping unreadable entry {Describe(entry)} in {path}.");
                continue;
            }

            bitmap.SetImmutable();

            icons.Add(new DecodedIcon(
                entry,
                EncodingName(entry),
                new RasterContent(bitmap, SKImage.FromBitmap(bitmap)),
                BuildVariant(entry, bitmap)));
        }

        if (icons.Count == 0)
            throw new InvalidOperationException($"[IcoDecoder] Every entry failed to decode in: {path}");

        var ordered = icons
            .OrderByDescending(icon => (long)icon.Variant.Width * icon.Variant.Height)
            .ThenByDescending(icon => icon.Entry.BitCount)
            .ThenByDescending(icon => icon.Entry.PayloadLength)
            .ThenBy(icon => icon.Entry.PayloadOffset)
            .ToList();

        composite.Content = new VariantRasterContent(
            [.. ordered.Select(icon => icon.Variant)],
            [.. ordered.Select(icon => icon.Content)],
            active: 0
        );

        Report(composite, data, ordered);

        return Task.CompletedTask;
    }

    /// <summary>
    /// The largest entry, which is the one that represents the file.
    /// </summary>
    public SKBitmap? DecodeThumbnail(string path, int maxDimension, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        var data = File.ReadAllBytes(path);
        var entries = IcoReader.ReadEntries(data);

        if (entries.Count == 0)
        {
            using var skData = SKData.CreateCopy(data);
            return SKBitmap.Decode(skData);
        }

        foreach (var entry in entries)
        {
            ct.ThrowIfCancellationRequested();

            try
            {
                if (DecodeEntry(data, entry) is { } bitmap)
                    return bitmap;
            }
            catch (Exception ex)
            {
                Logger.Warning($"[IcoDecoder] Thumbnail entry {Describe(entry)} failed: {ex.Message}");
            }
        }

        return null;
    }

    /// <summary>One entry that decoded, kept whole so the parallel lists cannot drift apart.</summary>
    private sealed record DecodedIcon(IcoEntry Entry, string Encoding, ICompositeContent Content, ImageVariant Variant);

    private static void Report(Composite composite, byte[] data, List<DecodedIcon> icons)
    {
        composite.AddFormatSpecific("Entries", icons.Count.ToString());
        composite.AddFormatSpecific("Size Range", icons.Count == 1 ? icons[0].Variant.Label : $"{icons[^1].Variant.Label} to {icons[0].Variant.Label}");

        var depths = icons
            .Where(icon => icon.Entry.BitCount > 0)
            .Select(icon => icon.Entry.BitCount)
            .Distinct()
            .Order()
            .Select(bits => $"{bits}-bit")
            .ToList();

        if (depths.Count > 0)
            composite.AddFormatSpecific("Depths", string.Join(", ", depths));

        var encodings = icons
            .GroupBy(icon => icon.Encoding, StringComparer.Ordinal)
            .OrderByDescending(group => group.Count())
            .ThenBy(group => group.Key, StringComparer.Ordinal)
            .Select(group => group.Count() == 1 ? group.Key : $"{group.Key} x{group.Count()}");

        composite.AddFormatSpecific("Encodings", string.Join(", ", encodings));

        var iconBytes = icons.Sum(icon => (long)icon.Entry.PayloadLength);
        composite.AddFormatSpecific("Icon Data", $"{Formatters.SizeToStr(iconBytes)} of {Formatters.SizeToStr(data.Length)}");
    }

    private static Task DecodeAsPlainImage(Composite composite, byte[] data, string path)
    {
        using var skData = SKData.CreateCopy(data);
        var bitmap = SKBitmap.Decode(skData)
                     ?? throw new InvalidOperationException($"[IcoDecoder] No readable entries in: {path}");

        Logger.Debug($"[IcoDecoder] {Path.GetFileName(path)} is not a container; decoded as a plain image.");

        bitmap.SetImmutable();
        composite.Content = new RasterContent(bitmap, SKImage.FromBitmap(bitmap));

        return Task.CompletedTask;
    }

    private static ImageVariant BuildVariant(IcoEntry entry, SKBitmap bitmap)
    {
        var depth = entry.BitCount > 0 ? $"{entry.BitCount}-bit " : string.Empty;
        return new ImageVariant($"{bitmap.Width} x {bitmap.Height}", bitmap.Width, bitmap.Height, $"{depth}{EncodingName(entry)}", entry.PayloadLength);
    }

    private static string EncodingName(IcoEntry entry) => entry.Kind == IcoPayloadKind.Dib ? "BMP" : "PNG";

    private static string Describe(IcoEntry entry) => $"{entry.Width}x{entry.Height} at {entry.BitCount}-bit";

    private static SKBitmap? DecodeEntry(byte[] data, IcoEntry entry)
    {
        if (entry.Kind == IcoPayloadKind.Embedded)
        {
            using var skData = SKData.CreateCopy(entry.Payload(data).ToArray());
            return SKBitmap.Decode(skData);
        }

        return IcoReader.Decode(data, entry) is { } image ? FromDecodedImage(image) : null;
    }

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