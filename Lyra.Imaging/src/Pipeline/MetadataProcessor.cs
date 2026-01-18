using Lyra.Common;
using Lyra.Imaging.Content;
using MetadataExtractor;
using MetadataExtractor.Formats.Exif;
using MetadataExtractor.Formats.Heif;
using MetadataExtractor.Formats.Icc;
using MetadataExtractor.Formats.Ico;
using MetadataExtractor.Formats.Jpeg;
using MetadataExtractor.Formats.Png;
using MetadataExtractor.Formats.WebP;
using Directory = MetadataExtractor.Directory;

namespace Lyra.Imaging.Pipeline;

internal static class MetadataProcessor
{
    public static ExifInfo ParseMetadata(string path)
    {
        try
        {
            return ProcessMetadata(ImageMetadataReader.ReadMetadata(path));
        }
        catch (Exception e)
        {
            Logger.Warning($"[MetadataProcessor] Error parsing metadata from file: {path}");
            Logger.Error($"[MetadataProcessor] Error parsing metadata: {e.Message}");
            return ExifInfo.Error;
        }
    }

    public static ExifInfo ParseMetadata(Stream stream, string path)
    {
        try
        {
            return ProcessMetadata(ImageMetadataReader.ReadMetadata(stream));
        }
        catch (Exception e)
        {
            Logger.Warning($"[MetadataProcessor] Error parsing metadata from file: {path}");
            Logger.Error($"[MetadataProcessor] Error while parsing metadata: {e.Message}");
            return ExifInfo.Error;
        }
    }

    private static ExifInfo ProcessMetadata(IReadOnlyList<Directory> directories)
    {
        var exifInfo = ProcessBasic(directories);
        ProcessTypeSpecific(directories, exifInfo);

        return exifInfo;
    }

    private static ExifInfo ProcessBasic(IReadOnlyList<Directory> directories)
    {
        var exifInfo = new ExifInfo();

        var ifd0 = directories.OfType<ExifIfd0Directory>().FirstOrDefault();
        var subIfd = directories.OfType<ExifSubIfdDirectory>().FirstOrDefault();
        var gps = directories.OfType<GpsDirectory>().FirstOrDefault();
        var icc = directories.OfType<IccDirectory>().FirstOrDefault();

        exifInfo.Make = ifd0?.GetDescription(ExifDirectoryBase.TagMake) ?? string.Empty;
        exifInfo.Model = ifd0?.GetDescription(ExifDirectoryBase.TagModel) ?? string.Empty;
        exifInfo.Lens = subIfd?.GetDescription(ExifDirectoryBase.TagLensModel) ?? string.Empty;

        exifInfo.ExposureTime = subIfd?.GetDescription(ExifDirectoryBase.TagExposureTime) ?? string.Empty;
        exifInfo.FNumber = subIfd?.GetDescription(ExifDirectoryBase.TagFNumber) ?? string.Empty;
        exifInfo.Iso = subIfd?.GetDescription(ExifDirectoryBase.TagIsoEquivalent) ?? string.Empty;
        exifInfo.Flash = subIfd?.GetDescription(ExifDirectoryBase.TagFlash) ?? string.Empty;

        exifInfo.Taken = subIfd?.GetDescription(ExifDirectoryBase.TagDateTimeOriginal) ?? string.Empty;

        var colorSpaceExif = subIfd?.GetDescription(ExifDirectoryBase.TagColorSpace) ?? string.Empty;
        var colorSpaceIcc = icc?.GetDescription(IccDirectory.TagColorSpace) ?? string.Empty;
        exifInfo.ColorSpace = AssignValue(colorSpaceExif, colorSpaceIcc, Priority.Low);

        exifInfo.IccProfile = icc?.GetDescription(IccDirectory.TagTagDesc) ?? string.Empty;

        exifInfo.GpsLatitude = gps?.GetDescription(GpsDirectory.TagLatitude) ?? string.Empty;
        exifInfo.GpsLongitude = gps?.GetDescription(GpsDirectory.TagLongitude) ?? string.Empty;

        exifInfo.Compression = subIfd?.GetDescription(ExifDirectoryBase.TagCompression) ?? string.Empty;
        exifInfo.Software = ifd0?.GetDescription(ExifDirectoryBase.TagSoftware) ?? string.Empty;

        return exifInfo;
    }

    private static void ProcessTypeSpecific(IReadOnlyList<Directory> directories, ExifInfo exifInfo)
    {
        var jpegDirectories = directories.OfType<JpegDirectory>();
        foreach (var directory in jpegDirectories)
        {
            var compression = directory.GetDescription(JpegDirectory.TagCompressionType) ?? string.Empty;
            exifInfo.Compression = AssignValue(exifInfo.Compression, compression, Priority.Low);
        }

        var pngDirectories = directories.OfType<PngDirectory>();
        foreach (var directory in pngDirectories)
        {
            var iccProfile = directory.GetDescription(PngDirectory.TagIccProfileName) ?? string.Empty;
            exifInfo.IccProfile = AssignValue(exifInfo.IccProfile, iccProfile, Priority.Low);

            var bitsPerSample = directory.GetDescription(PngDirectory.TagBitsPerSample) ?? string.Empty;
            exifInfo.ColorDepth = AssignValue(exifInfo.ColorDepth, bitsPerSample, Priority.High);

            var colorType = directory.GetDescription(PngDirectory.TagColorType) ?? string.Empty;
            exifInfo.ColorType = AssignValue(exifInfo.ColorType, colorType, Priority.High);

            var compression = directory.GetDescription(PngDirectory.TagCompressionType) ?? string.Empty;
            exifInfo.Compression = AssignValue(exifInfo.Compression, compression, Priority.Low);
        }

        var webpDirectories = directories.OfType<WebPDirectory>();
        foreach (var directory in webpDirectories)
        {
            // todo ??
        }

        var heicDirectories = directories.OfType<HeicImagePropertiesDirectory>();
        foreach (var directory in heicDirectories)
        {
            // todo
        }

        var icoDirectories = directories.OfType<IcoDirectory>();
        foreach (var directory in icoDirectories)
        {
            // todo
        }

        // todo TgaHeaderDirectory
        // todo BmpHeaderDirectory
    }

    private static string AssignValue(string currentValue, string newValue, Priority newValuePriority)
    {
        if (string.IsNullOrEmpty(currentValue))
            return newValue;

        if (newValue.Equals(currentValue) || string.IsNullOrWhiteSpace(newValue))
            return currentValue;

        if (newValuePriority == Priority.High)
        {
            Logger.Warning($"[MetadataProcessor] Value \"{currentValue}\" replaced by \"{newValue}\"");
            return newValue;
        }

        return currentValue;
    }

    private enum Priority
    {
        Low,
        High
    }
}