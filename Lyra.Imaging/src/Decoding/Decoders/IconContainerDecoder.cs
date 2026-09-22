using Lyra.Common;
using Lyra.Imaging.Content;
using Lyra.Imaging.Decoding.Support;
using Lyra.ManagedCodecs.Raster;
using SkiaSharp;

namespace Lyra.Imaging.Decoding.Decoders;

/// <summary>
/// The shape both icon containers share. An .ico and an .icns are the same document in different
/// clothes: a directory of renditions of one picture, each at its own size and encoding, any of
/// which may be unreadable on its own without the file being unreadable.
///
/// So the order of work is fixed here - read, decode what decodes, warn about what does not,
/// publish the survivors as variants - and a format supplies only what it alone knows: how to
/// list its entries, how to decode one, what to call its encoding, and how to sort the result.
/// </summary>
/// <typeparam name="TEntry">The container's own directory entry.</typeparam>
internal abstract class IconContainerDecoder<TEntry> : DecoderBase
{
    protected sealed record DecodedIcon(TEntry Entry, string Encoding, ICompositeContent Content, ImageVariant Variant);

    protected abstract string EntryNoun { get; }
    
    protected abstract string CountLabel { get; }

    protected abstract IReadOnlyList<TEntry> ReadEntries(byte[] data);

    protected abstract SKBitmap? DecodeEntry(byte[] data, TEntry entry);
    
    protected abstract string EncodingName(byte[] data, TEntry entry);

    protected abstract ImageVariant BuildVariant(TEntry entry, SKBitmap bitmap, string encoding);

    protected abstract string Describe(TEntry entry);
    
    protected abstract IEnumerable<DecodedIcon> InPresentationOrder(IEnumerable<DecodedIcon> icons);
    
    protected abstract void ReportFormatSpecific(Composite composite, IReadOnlyList<DecodedIcon> icons);

    protected virtual void ReadMetadata(Composite composite, byte[] data, string path) { }

    protected override void Decode(Composite composite, string path, CancellationToken ct)
    {
        var data = composite.ReadAllBytes(ct);

        ReadMetadata(composite, data, path);

        ct.ThrowIfCancellationRequested();

        var entries = ReadEntries(data);
        if (entries.Count == 0)
        {
            DecodeAsPlainImage(composite, data, path);
            return;
        }

        var icons = DecodeEntries(data, entries, path, ct);

        if (icons.Count == 0)
            throw new InvalidOperationException($"[{Name}] Every {EntryNoun} failed to decode in: {path}");

        var ordered = InPresentationOrder(icons).ToList();

        composite.Content = new VariantRasterContent(
            [.. ordered.Select(icon => icon.Variant)],
            [.. ordered.Select(icon => icon.Content)],
            active: 0
        );

        Report(composite, data, ordered);
    }
    
    private List<DecodedIcon> DecodeEntries(byte[] data, IReadOnlyList<TEntry> entries, string path, CancellationToken ct)
    {
        var icons = new List<DecodedIcon>(entries.Count);

        try
        {
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
                    Logger.Warning($"[{Name}] Entry {Describe(entry)} failed to decode: {ex.Message}");
                }

                if (bitmap is null)
                {
                    Logger.Warning($"[{Name}] Skipping unreadable {EntryNoun} {Describe(entry)} in {path}.");
                    continue;
                }

                bitmap.SetImmutable();

                var encoding = EncodingName(data, entry);

                icons.Add(new DecodedIcon(
                    entry,
                    encoding,
                    new RasterContent(bitmap, SKImage.FromBitmap(bitmap)),
                    BuildVariant(entry, bitmap, encoding))
                );
            }
        }
        catch
        {
            foreach (var icon in icons)
                icon.Content.Dispose();

            throw;
        }

        return icons;
    }

    private void Report(Composite composite, byte[] data, IReadOnlyList<DecodedIcon> icons)
    {
        composite.AddFormatSpecific(CountLabel, icons.Count.ToString());
        composite.AddFormatSpecific("Size Range", icons.Count == 1
            ? icons[0].Variant.Label
            : $"{icons[^1].Variant.Label} to {icons[0].Variant.Label}");

        ReportFormatSpecific(composite, icons);

        var iconBytes = icons.Sum(icon => icon.Variant.ByteSize);
        composite.AddFormatSpecific("Icon Data", $"{Formatters.SizeToStr(iconBytes)} of {Formatters.SizeToStr(data.Length)}");
    }

    protected static void AddEncodings(Composite composite, IReadOnlyList<DecodedIcon> icons)
    {
        var encodings = icons
            .GroupBy(icon => icon.Encoding, StringComparer.Ordinal)
            .OrderByDescending(group => group.Count())
            .ThenBy(group => group.Key, StringComparer.Ordinal)
            .Select(group => group.Count() == 1 ? group.Key : $"{group.Key} x{group.Count()}");

        composite.AddFormatSpecific("Encodings", string.Join(", ", encodings));
    }

    private void DecodeAsPlainImage(Composite composite, byte[] data, string path)
    {
        using var skData = SKData.CreateCopy(data);
        var bitmap = SKBitmap.Decode(skData)
                     ?? throw new InvalidOperationException($"[{Name}] No readable {EntryNoun}s in: {path}");

        Logger.Debug($"[{Name}] {Path.GetFileName(path)} is not a container; decoded as a plain image.");

        bitmap.SetImmutable();
        composite.Content = new RasterContent(bitmap, SKImage.FromBitmap(bitmap));
    }
    
    protected static unsafe SKBitmap FromDecodedImage(DecodedImage image)
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