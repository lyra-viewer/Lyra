using System.Globalization;
using Lyra.Imaging.Content;
using MetadataExtractor;
using MetadataExtractor.Formats.Exif;
using MetadataExtractor.Formats.Icc;
using Directory = MetadataExtractor.Directory;
using static Lyra.Imaging.Metadata.MetadataValues;

namespace Lyra.Imaging.Metadata;

/// <summary>
/// The facts the camera recorded: body and lens, exposure, capture time, orientation, color,
/// GPS, authorship - read from the standard EXIF directories.
///
/// Gaps EXIF leaves are filled afterwards by <see cref="FormatHeaderReader"/> from the formats'
/// own headers, which is a separate pass over different directories.
/// </summary>
internal static class ExifReader
{
    public static ExifInfo Read(IReadOnlyList<Directory> directories)
    {
        var exifInfo = new ExifInfo();

        var ifd0 = directories.OfType<ExifIfd0Directory>().FirstOrDefault();
        var subIfd = directories.OfType<ExifSubIfdDirectory>().FirstOrDefault();
        var gps = directories.OfType<GpsDirectory>().FirstOrDefault();
        var icc = directories.OfType<IccDirectory>().FirstOrDefault();

        exifInfo.Make = Describe(ifd0, ExifDirectoryBase.TagMake);
        exifInfo.Model = Describe(ifd0, ExifDirectoryBase.TagModel);
        exifInfo.Lens = Describe(subIfd, ExifDirectoryBase.TagLensModel);

        exifInfo.ExposureTime = Describe(subIfd, ExifDirectoryBase.TagExposureTime);
        exifInfo.FNumber = Describe(subIfd, ExifDirectoryBase.TagFNumber);
        exifInfo.Iso = Describe(subIfd, ExifDirectoryBase.TagIsoEquivalent);
        exifInfo.FocalLength = Describe(subIfd, ExifDirectoryBase.TagFocalLength);
        exifInfo.FocalLength35 = Describe(subIfd, ExifDirectoryBase.Tag35MMFilmEquivFocalLength);
        exifInfo.ExposureBias = ReadExposureBias(subIfd);
        exifInfo.ExposureProgram = Describe(subIfd, ExifDirectoryBase.TagExposureProgram);
        exifInfo.MeteringMode = Describe(subIfd, ExifDirectoryBase.TagMeteringMode);
        exifInfo.WhiteBalance = Describe(subIfd, ExifDirectoryBase.TagWhiteBalanceMode);
        exifInfo.Flash = Describe(subIfd, ExifDirectoryBase.TagFlash);

        exifInfo.Taken = ReadTaken(subIfd, ifd0);

        exifInfo.OrientationValue = ReadOrientation(ifd0);
        exifInfo.Orientation = DescribeOrientation(exifInfo.OrientationValue);

        var colorSpaceExif = Describe(subIfd, ExifDirectoryBase.TagColorSpace);
        var colorSpaceIcc = Describe(icc, IccDirectory.TagColorSpace);
        exifInfo.ColorSpace = AssignValue(colorSpaceExif, colorSpaceIcc, Priority.Low);
        exifInfo.IccProfile = ExtractIccProfileName(Describe(icc, IccDirectory.TagTagDesc));

        // Formatted as a pair so the two rows line up in the panel's monospace column.
        (exifInfo.GpsLatitude, exifInfo.GpsLongitude) = GpsCoordinateFormat.Align(
            Describe(gps, GpsDirectory.TagLatitude),
            Describe(gps, GpsDirectory.TagLongitude)
        );

        exifInfo.GpsAltitude = Describe(gps, GpsDirectory.TagAltitude);

        exifInfo.Artist = Describe(ifd0, ExifDirectoryBase.TagArtist);
        exifInfo.Copyright = Describe(ifd0, ExifDirectoryBase.TagCopyright);

        // Compression (0x0103) is an IFD0 tag - that is where TIFF puts it, and reading only the
        // SubIFD silently dropped the row for every TIFF. JPEG and PNG fill it in from their own
        // directories later, so this is the TIFF path in practice.
        exifInfo.Compression = FirstNonEmpty(
            Describe(ifd0, ExifDirectoryBase.TagCompression),
            Describe(subIfd, ExifDirectoryBase.TagCompression)
        );
        
        exifInfo.Software = Describe(ifd0, ExifDirectoryBase.TagSoftware);

        return exifInfo;
    }
    
    private static string ReadTaken(Directory? subIfd, Directory? ifd0)
    {
        if (TryReadDateTime(subIfd, ExifDirectoryBase.TagDateTimeOriginal, out var taken)
            || TryReadDateTime(subIfd, ExifDirectoryBase.TagDateTimeDigitized, out taken)
            || TryReadDateTime(ifd0, ExifDirectoryBase.TagDateTime, out taken))
        {
            return taken.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
        }

        return Describe(subIfd, ExifDirectoryBase.TagDateTimeOriginal);
    }

    private static bool TryReadDateTime(Directory? directory, int tag, out DateTime value)
    {
        if (directory is not null && directory.TryGetDateTime(tag, out value))
            return true;

        value = default;
        return false;
    }
    
    private static string ReadExposureBias(Directory? subIfd)
    {
        if (subIfd is null
            || !subIfd.TryGetRational(ExifDirectoryBase.TagExposureBias, out var bias)
            || bias.ToDouble() == 0)
        {
            return string.Empty;
        }

        return Describe(subIfd, ExifDirectoryBase.TagExposureBias);
    }

    /// <summary>
    /// Reads IFD0 tag 0x0112. Anything outside the 1-8 range defined by the spec is treated as
    /// absent rather than trusted - a wrong rotation is worse than none.
    /// </summary>
    private static ExifOrientation ReadOrientation(Directory? ifd0)
    {
        if (ifd0 is null || !ifd0.TryGetInt32(ExifDirectoryBase.TagOrientation, out var raw))
            return ExifOrientation.Unknown;

        return raw is >= 1 and <= 8 ? (ExifOrientation)raw : ExifOrientation.Unknown;
    }

    /// <summary>
    /// Describes the transform the file asks for. The raw number is included because tools
    /// disagree on the wording - macOS Preview describes how the pixels are stored, exiftool
    /// describes the correction to apply - while the number is unambiguous.
    ///
    /// Normal and Unknown produce no row: an orientation worth mentioning is one that isn't
    /// the default.
    /// </summary>
    private static string DescribeOrientation(ExifOrientation orientation) => orientation switch
    {
        ExifOrientation.MirrorHorizontal            => "Mirror horizontal (2)",
        ExifOrientation.Rotate180                   => "Rotate 180° (3)",
        ExifOrientation.MirrorVertical              => "Mirror vertical (4)",
        ExifOrientation.MirrorHorizontalRotate270Cw => "Mirror horizontal, rotate 270° CW (5)",
        ExifOrientation.Rotate90Cw                  => "Rotate 90° CW (6)",
        ExifOrientation.MirrorHorizontalRotate90Cw  => "Mirror horizontal, rotate 90° CW (7)",
        ExifOrientation.Rotate270Cw                 => "Rotate 270° CW (8)",
        _ => string.Empty
    };
}
