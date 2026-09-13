using Lyra.Common;
using Lyra.Imaging.Content;
using Lyra.Imaging.Decoding.Support;
using Lyra.Imaging.Metadata;
using Lyra.ManagedCodecs.Raster.Ico;
using SkiaSharp;

namespace Lyra.Imaging.Decoding.Decoders;

/// <summary>
/// Windows icon containers. Entries are either a device-independent bitmap the reader decodes
/// itself, or a whole PNG embedded in the payload.
/// </summary>
internal sealed class IcoDecoder : IconContainerDecoder<IcoEntry>, IThumbnailDecoder
{
    public override bool CanDecode(ImageFormatType format) => format == ImageFormatType.Ico;

    protected override string EntryNoun => "entry";

    protected override string CountLabel => "Entries";

    protected override IReadOnlyList<IcoEntry> ReadEntries(byte[] data) => IcoReader.ReadEntries(data);

    protected override void ReadMetadata(Composite composite, byte[] data, string path)
    {
        using var stream = new MemoryStream(data, writable: false);
        composite.ExifInfo = MetadataProcessor.ParseMetadata(stream, path);
    }

    protected override SKBitmap? DecodeEntry(byte[] data, IcoEntry entry)
    {
        if (entry.Kind == IcoPayloadKind.Embedded)
        {
            using var skData = SKData.CreateCopy(entry.Payload(data).ToArray());
            return SKBitmap.Decode(skData);
        }

        return IcoReader.Decode(data, entry) is { } image ? FromDecodedImage(image) : null;
    }

    protected override string EncodingName(byte[] data, IcoEntry entry) => entry.Kind == IcoPayloadKind.Dib ? "BMP" : "PNG";

    protected override ImageVariant BuildVariant(IcoEntry entry, SKBitmap bitmap, string encoding)
    {
        var depth = entry.BitCount > 0 ? $"{entry.BitCount}-bit " : string.Empty;
        return new ImageVariant($"{bitmap.Width} x {bitmap.Height}", bitmap.Width, bitmap.Height, $"{depth}{encoding}", entry.PayloadLength);
    }

    protected override string Describe(IcoEntry entry) => $"{entry.Width}x{entry.Height} at {entry.BitCount}-bit";

    /// <summary>
    /// Largest first, then richest: two entries of the same size are distinguished by depth, and
    /// then by weight, so the one carrying the most picture opens.
    /// </summary>
    protected override IEnumerable<DecodedIcon> InPresentationOrder(IEnumerable<DecodedIcon> icons) =>
        icons
            .OrderByDescending(icon => (long)icon.Variant.Width * icon.Variant.Height)
            .ThenByDescending(icon => icon.Entry.BitCount)
            .ThenByDescending(icon => icon.Entry.PayloadLength)
            .ThenBy(icon => icon.Entry.PayloadOffset);

    protected override void ReportFormatSpecific(Composite composite, IReadOnlyList<DecodedIcon> icons)
    {
        var depths = icons
            .Where(icon => icon.Entry.BitCount > 0)
            .Select(icon => icon.Entry.BitCount)
            .Distinct()
            .Order()
            .Select(bits => $"{bits}-bit")
            .ToList();

        if (depths.Count > 0)
            composite.AddFormatSpecific("Depths", string.Join(", ", depths));

        AddEncodings(composite, icons);
    }

    /// <summary>
    /// The largest entry, which is the one that represents the file.
    /// </summary>
    public SKBitmap? DecodeThumbnail(string path, int maxDimension, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        var data = DecoderIO.ReadAllBytes(path, ct, out _);
        var entries = ReadEntries(data);

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
}