using Lyra.Common;
using Lyra.Imaging.Content.Exif;
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
    public static ExifInfo ParseMetadata(string path) => Parse(path, () =>
    {
        using var stream = File.OpenRead(path);
        return ImageMetadata.Read(stream);
    });

    public static ExifInfo ParseMetadata(Stream stream, string path) => Parse(path, () => ImageMetadata.Read(stream));

    /// <summary>
    /// Parses the blocks a decoder located itself, for containers it already holds in memory -
    /// JPEG XL and JPEG 2000.
    /// </summary>
    public static ExifInfo ParseMetadata(ContainerContents container, string path) => Parse(path, () => ImageMetadata.From(container));

    /// <summary>A PSD's metadata resources, which PsdDecoder finds through Lyra.Psd, in file order.</summary>
    public static ExifInfo ParsePhotoshopResources(IEnumerable<PhotoshopResource> resources, string path) => ParseMetadata(ContainerContents.FromPhotoshop(resources), path);

    /// <summary>
    /// Camera raw: what EXIF leaves out is filled from LibRaw's own reading, which also stands in
    /// when EXIF cannot be read at all - as for a raw with no metadata Lyra reads (CRW, X3F).
    /// </summary>
    public static ExifInfo ParseRawMetadata(Stream stream, string path, RawCapture capture)
    {
        var metadata = Guarded(path, () => ImageMetadata.Read(stream));
        if (metadata is { Unreadable: true })
        {
            Warn(path, DamagedStructure);
            metadata = null;
        }

        return Guarded(path, () =>
        {
            var exifInfo = Process(metadata ?? ImageMetadata.Empty, info => RawMetadata.Apply(info, capture));
            return metadata is null or { IsEmpty: true } && !exifInfo.HasData() ? ExifInfo.Failed() : exifInfo;
        }) ?? ExifInfo.Failed();
    }

    private const string UnknownFormat = "The file is of no format whose metadata Lyra reads.";
    private const string DamagedStructure = "The file's structure is damaged.";

    private static ExifInfo Parse(string path, Func<ImageMetadata?> read) => Guarded(path, () => read() switch
    {
        null => Failed(path, UnknownFormat),
        { Unreadable: true } => Failed(path, DamagedStructure),
        var metadata => Process(metadata)
    }) ?? ExifInfo.Failed();

    /// <summary>What <paramref name="read"/> returns, or null when it throws.</summary>
    private static T? Guarded<T>(string path, Func<T?> read) where T : class
    {
        try
        {
            return read();
        }
        catch (Exception e)
        {
            Warn(path, e.Message);
            return null;
        }
    }

    private static ExifInfo Failed(string path, string reason)
    {
        Warn(path, reason);
        return ExifInfo.Failed();
    }

    private static void Warn(string path, string reason) => Logger.Warning($"[MetadataProcessor] Could not read metadata from {path}: {reason}");

    private static ExifInfo Process(ImageMetadata metadata, Action<ExifInfo>? adjust = null)
    {
        var exifInfo = ExifReader.Read(metadata.Exif, metadata.Icc);
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