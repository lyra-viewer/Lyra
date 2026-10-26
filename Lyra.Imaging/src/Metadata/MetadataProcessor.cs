using Lyra.Common;
using Lyra.Imaging.Content;
using Lyra.Metadata;
using Lyra.Metadata.Containers;
using Lyra.Metadata.Photoshop;

namespace Lyra.Imaging.Metadata;

/// <summary>
/// Turns a file's metadata into the flat record the panel displays.
///
/// The work is split by source: <see cref="ExifReader"/> reads what the camera recorded,
/// <see cref="FormatHeaderReader"/> what the format's own header says, <see cref="DescriptiveReader"/>
/// what a person wrote, and <see cref="MetadataValues"/> holds the normalization they all rely on.
/// This class owns the entry points, the order those passes run in, and the final reconciliation
/// between them.
///
/// Lyra.Metadata reads and resolves the blocks (<see cref="ImageMetadata"/>): anything laid out as a
/// TIFF, and the containers it opens; PSD's resources come located by Lyra.Psd. A file of any other
/// format has no metadata Lyra reads.
/// </summary>
internal static class MetadataProcessor
{
    public static ExifInfo ParseMetadata(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            return Process(Read(stream) ?? throw new InvalidDataException(UnknownFormat));
        }
        catch (Exception e)
        {
            Logger.Warning($"[MetadataProcessor] Could not read metadata from {path}: {e.Message}");
            return ExifInfo.Failed();
        }
    }

    public static ExifInfo ParseMetadata(Stream stream, string path)
    {
        try
        {
            return Process(Read(stream) ?? throw new InvalidDataException(UnknownFormat));
        }
        catch (Exception e)
        {
            Logger.Warning($"[MetadataProcessor] Could not read metadata from {path}: {e.Message}");
            return ExifInfo.Failed();
        }
    }

    /// <summary>
    /// Parses the blocks a decoder located itself, for containers it already holds in memory -
    /// JPEG XL and JPEG 2000.
    /// </summary>
    public static ExifInfo ParseMetadata(ContainerContents container, string path)
    {
        try
        {
            return Process(Readable(ImageMetadata.From(container)));
        }
        catch (Exception e)
        {
            Logger.Warning($"[MetadataProcessor] Could not read embedded metadata from {path}: {e.Message}");
            return ExifInfo.Failed();
        }
    }

    /// <summary>A PSD's metadata resources, which PsdDecoder finds through Lyra.Psd, in file order.</summary>
    public static ExifInfo ParsePhotoshopResources(IEnumerable<PhotoshopResource> resources, string path) => ParseMetadata(ContainerContents.FromPhotoshop(resources), path);

    /// <summary>
    /// Camera raw: what EXIF leaves out is filled from LibRaw's own reading, which also stands in
    /// when EXIF cannot be read at all - as for a raw with no metadata Lyra reads (CRW, X3F).
    /// </summary>
    public static ExifInfo ParseRawMetadata(Stream stream, string path, RawCapture capture)
    {
        ImageMetadata? metadata = null;
        try
        {
            metadata = Read(stream);
        }
        catch (Exception e)
        {
            Logger.Warning($"[MetadataProcessor] Could not read metadata from {path}: {e.Message}");
        }

        try
        {
            var exifInfo = Process(metadata ?? ImageMetadata.Empty, info => RawMetadata.Apply(info, capture));
            return metadata is null or { IsEmpty: true } && !exifInfo.HasData() ? ExifInfo.Failed() : exifInfo;
        }
        catch (Exception e)
        {
            Logger.Warning($"[MetadataProcessor] Could not read metadata from {path}: {e.Message}");
            return ExifInfo.Failed();
        }
    }

    private const string UnknownFormat = "The file is of no format whose metadata Lyra reads.";

    /// <summary>The file's metadata, or null for a format Lyra.Metadata does not read; throws when its structure is unreadable.</summary>
    private static ImageMetadata? Read(Stream stream) => ImageMetadata.Read(stream) is { } metadata ? Readable(metadata) : null;

    private static ImageMetadata Readable(ImageMetadata metadata) => metadata.Unreadable ? throw new InvalidDataException("The file's structure is damaged.") : metadata;

    private static ExifInfo Process(ImageMetadata metadata, Action<ExifInfo>? adjust = null)
    {
        var exifInfo = ExifReader.Read(metadata.Exif, IccFacts.From(metadata.Icc));
        ExifReader.ApplyXmp(metadata.Xmp, exifInfo);
        FormatHeaderReader.Apply(metadata.Facts, exifInfo);
        DescriptiveReader.Apply(metadata.Xmp, metadata.Iptc, exifInfo);
        adjust?.Invoke(exifInfo);
        ResolveOverlaps(exifInfo);

        return exifInfo.Seal();
    }

    private static void ResolveOverlaps(ExifInfo exifInfo)
    {
        // A named ICC profile answers the color question authoritatively and far more precisely
        // than the generic color space row does - "Display P3" against "RGB", or the same fact
        // twice for sRGB - so the two never share the panel.
        if (exifInfo.IccProfile.Length > 0)
            exifInfo.ColorSpace = string.Empty;
    }
}