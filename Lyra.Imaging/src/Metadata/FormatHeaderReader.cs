using Lyra.Imaging.Content;
using MetadataExtractor; // DirectoryExtensions: TryGetInt32
using MetadataExtractor.Formats.Bmp;
using MetadataExtractor.Formats.Heif;
using MetadataExtractor.Formats.Ico;
using MetadataExtractor.Formats.Jpeg;
using MetadataExtractor.Formats.Png;
using MetadataExtractor.Formats.Tga;
using Directory = MetadataExtractor.Directory;
using static Lyra.Imaging.Metadata.MetadataValues;

namespace Lyra.Imaging.Metadata;

/// <summary>
/// Facts the formats record in their own headers rather than in EXIF - bit depth, color type,
/// compression - for the many images that carry no EXIF block at all.
///
/// Runs after <see cref="ExifReader"/> and only fills gaps: every merge goes through
/// <see cref="MetadataValues.AssignValue"/> so a value EXIF already supplied is not overwritten
/// by a weaker one.
/// </summary>
internal static class FormatHeaderReader
{
    public static void Apply(IReadOnlyList<Directory> directories, ExifInfo exifInfo)
    {
        var jpegDirectories = directories.OfType<JpegDirectory>();
        foreach (var directory in jpegDirectories)
        {
            var compression = Describe(directory, JpegDirectory.TagCompressionType);
            exifInfo.Compression = AssignValue(exifInfo.Compression, compression, Priority.Low);

            var dataPrecision = Describe(directory, JpegDirectory.TagDataPrecision);
            exifInfo.ColorDepth = AssignValue(exifInfo.ColorDepth, dataPrecision, Priority.Low);

            var colorType = DescribeJpegColorType(directory);
            exifInfo.ColorType = AssignValue(exifInfo.ColorType, colorType, Priority.Low);
        }

        var pngDirectories = directories.OfType<PngDirectory>();
        foreach (var directory in pngDirectories)
        {
            var iccProfile = Describe(directory, PngDirectory.TagIccProfileName);
            exifInfo.IccProfile = AssignValue(exifInfo.IccProfile, iccProfile, Priority.Low);

            var bitsPerSample = Describe(directory, PngDirectory.TagBitsPerSample);
            exifInfo.ColorDepth = AssignValue(exifInfo.ColorDepth, bitsPerSample, Priority.High);

            var colorType = Describe(directory, PngDirectory.TagColorType);
            exifInfo.ColorType = AssignValue(exifInfo.ColorType, colorType, Priority.High);

            var compression = Describe(directory, PngDirectory.TagCompressionType);
            exifInfo.Compression = AssignValue(exifInfo.Compression, compression, Priority.Low);
        }

        foreach (var directory in directories.OfType<BmpHeaderDirectory>())
        {
            var bitsPerPixel = Describe(directory, BmpHeaderDirectory.TagBitsPerPixel);
            exifInfo.ColorDepth = AssignValue(exifInfo.ColorDepth, bitsPerPixel, Priority.Low);

            var compression = Describe(directory, BmpHeaderDirectory.TagCompression);
            exifInfo.Compression = AssignValue(exifInfo.Compression, compression, Priority.Low);
        }
        
        var openingDepth = directories.OfType<IcoDirectory>()
            .Select(entry => (Pixels: IcoPixels(entry), Depth: Describe(entry, IcoDirectory.TagBitsPerPixel)))
            .Where(entry => entry.Pixels > 0)
            .OrderByDescending(entry => entry.Pixels)
            .Select(entry => entry.Depth)
            .FirstOrDefault(string.Empty);

        if (openingDepth.Length > 0)
            exifInfo.ColorDepth = AssignValue(exifInfo.ColorDepth, openingDepth, Priority.Low);

        foreach (var directory in directories.OfType<TgaHeaderDirectory>())
        {
            var imageDepth = Describe(directory, TgaHeaderDirectory.TagImageDepth);
            exifInfo.ColorDepth = AssignValue(exifInfo.ColorDepth, imageDepth, Priority.Low);
        }

        foreach (var directory in directories.OfType<HeicImagePropertiesDirectory>())
        {
            var bitDepth = Describe(directory, HeicImagePropertiesDirectory.TagBitDepthLuma);
            exifInfo.ColorDepth = AssignValue(exifInfo.ColorDepth, bitDepth, Priority.Low);

            if (exifInfo.ContainerRotation == ExifOrientation.Unknown)
                exifInfo.ContainerRotation = ReadContainerRotation(directory);
        }

        // WebP and Photoshop are deliberately absent. WebP's directory holds only dimensions plus
        // alpha and animation flags, which are true of almost every file and so say nothing worth
        // a row; PSD has its own path through PsdDecoder and the Lyra.Psd package.
    }
    
    private static int IcoPixels(Directory entry)
    {
        var width = entry.TryGetInt32(IcoDirectory.TagImageWidth, out var w) ? w : 0;
        var height = entry.TryGetInt32(IcoDirectory.TagImageHeight, out var h) ? h : 0;

        return (width == 0 ? 256 : width) * (height == 0 ? 256 : height);
    }

    private static string DescribeJpegColorType(Directory jpeg) =>
        jpeg.TryGetInt32(JpegDirectory.TagNumberOfComponents, out var components)
            ? components switch
            {
                1 => "Grayscale",
                3 => "YCbCr",
                4 => "CMYK / YCCK",
                _ => string.Empty
            }
            : string.Empty;
    
    private static ExifOrientation ReadContainerRotation(Directory properties) =>
        properties.TryGetInt32(HeicImagePropertiesDirectory.TagRotation, out var degrees)
            ? degrees switch
            {
                0 => ExifOrientation.Normal,
                90 => ExifOrientation.Rotate270Cw,
                180 => ExifOrientation.Rotate180,
                270 => ExifOrientation.Rotate90Cw,
                _ => ExifOrientation.Unknown
            }
            : ExifOrientation.Unknown;
}