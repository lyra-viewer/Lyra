using Lyra.Common;
using Lyra.Imaging.Content;
using MetadataExtractor;
using MetadataExtractor.Formats.Png;
using MetadataExtractor.Formats.Xmp;
using Directory = MetadataExtractor.Directory;

namespace Lyra.Imaging.Metadata;

/// <summary>
/// Turns a file's metadata directories into the flat record the panel displays.
///
/// The work is split by source: <see cref="ExifReader"/> reads what the camera recorded,
/// <see cref="FormatHeaderReader"/> what the format's own header says, <see cref="DescriptiveReader"/>
/// what a person wrote, and <see cref="MetadataValues"/> holds the normalization they all rely on.
/// This class owns the entry points, the order those passes run in, and the final reconciliation
/// between them.
///
/// MetadataExtractor supplies the directories for every format but one: a BigTIFF goes to
/// <see cref="BigTiffMetadataReader"/>, which produces the same directories from 64-bit offsets.
/// </summary>
internal static class MetadataProcessor
{
    public static ExifInfo ParseMetadata(string path)
    {
        try
        {
            return ProcessMetadata(BigTiffMetadataReader.IsBigTiff(path)
                ? BigTiffMetadataReader.Read(path)
                : ImageMetadataReader.ReadMetadata(path));
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
            return ProcessMetadata(BigTiffMetadataReader.IsBigTiff(stream)
                ? BigTiffMetadataReader.Read(stream)
                : ReadTolerating(stream, path));
        }
        catch (Exception e)
        {
            Logger.Warning($"[MetadataProcessor] Could not read metadata from {path}: {e.Message}");
            return ExifInfo.Failed();
        }
    }

    /// <summary>
    /// Parses metadata handed over as raw blocks rather than as a file, for containers
    /// MetadataExtractor cannot open itself - JPEG XL and JPEG 2000, whose decoders pull the
    /// blocks out of the container's boxes with <see cref="IsoBoxMetadata"/>.
    /// </summary>
    public static ExifInfo ParseMetadata(byte[]? exif, byte[]? xmp, string path)
    {
        try
        {
            var directories = new List<Directory>();

            if (exif is { Length: > 0 })
            {
                using var stream = new MemoryStream(exif, writable: false);
                directories.AddRange(ImageMetadataReader.ReadMetadata(stream));
            }

            if (xmp is { Length: > 0 })
                directories.Add(new XmpReader().Extract(xmp));

            return ProcessMetadata(directories);
        }
        catch (Exception e)
        {
            Logger.Warning($"[MetadataProcessor] Could not read embedded metadata from {path}: {e.Message}");
            return ExifInfo.Failed();
        }
    }

    /// <summary>Reads the stream, retrying a PNG that <see cref="PngChunkRepair"/> can mend.</summary>
    private static IReadOnlyList<Directory> ReadTolerating(Stream stream, string path)
    {
        var start = stream.CanSeek ? stream.Position : -1;

        try
        {
            return ImageMetadataReader.ReadMetadata(stream);
        }
        catch (Exception e) when (start >= 0 && e is PngProcessingException or IOException)
        {
            stream.Position = start;

            using var repaired = PngChunkRepair.Repair(stream, out var repair);
            if (repaired is null)
                throw;

            Logger.Info($"[MetadataProcessor] {path}: {e.Message}; read around it ({repair}).");
            return ImageMetadataReader.ReadMetadata(repaired);
        }
    }

    private static ExifInfo ProcessMetadata(IReadOnlyList<Directory> directories)
    {
        var exifInfo = ExifReader.Read(directories);
        FormatHeaderReader.Apply(directories, exifInfo);
        DescriptiveReader.Apply(directories, exifInfo);
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