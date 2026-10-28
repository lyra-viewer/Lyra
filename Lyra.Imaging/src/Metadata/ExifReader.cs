using System.Globalization;
using Lyra.Imaging.Content.Exif;
using Lyra.Metadata.Exif;
using Lyra.Metadata.Icc;
using Lyra.Metadata.Tiff;
using Lyra.Metadata.Xmp;
using static Lyra.Imaging.Metadata.MetadataValues;

namespace Lyra.Imaging.Metadata;

/// <summary>
/// The facts the camera recorded: body and lens, exposure, capture time, orientation, color,
/// GPS, authorship - read from IFD0 and the EXIF and GPS directories.
///
/// Gaps EXIF leaves are filled afterwards by <see cref="FormatHeaderReader"/> from the formats'
/// own headers, which is a separate pass over different directories.
/// </summary>
internal static class ExifReader
{
    private const TiffDirectoryKind Ifd0 = TiffDirectoryKind.Image;
    private const TiffDirectoryKind Exif = TiffDirectoryKind.Exif;
    private const TiffDirectoryKind Gps = TiffDirectoryKind.Gps;

    /// <summary>XMP's copy of the camera and lens, for a writer that kept them only there.</summary>
    public static void ApplyXmp(XmpPacket? xmp, ExifInfo exifInfo)
    {
        if (xmp is null)
            return;

        exifInfo.Make = FirstNonEmpty(exifInfo.Make, Clean(xmp.First(XmpNamespaces.Tiff, "Make")));
        exifInfo.Model = FirstNonEmpty(exifInfo.Model, Clean(xmp.First(XmpNamespaces.Tiff, "Model")));
        exifInfo.Lens = FirstNonEmpty(exifInfo.Lens, Clean(xmp.First(XmpNamespaces.ExifEx, "LensModel")), Clean(xmp.First(XmpNamespaces.Aux, "Lens")));
    }

    public static ExifInfo Read(ExifMetadata? exif, IccProfile? icc)
    {
        var exifInfo = new ExifInfo();

        exifInfo.Make = Describe(exif, Ifd0, ExifTags.Make);
        exifInfo.Model = Describe(exif, Ifd0, ExifTags.Model);
        exifInfo.Lens = Describe(exif, Exif, ExifTags.LensModel);

        exifInfo.ExposureTime = Describe(exif, Exif, ExifTags.ExposureTime);
        exifInfo.FNumber = Describe(exif, Exif, ExifTags.FNumber);
        exifInfo.Iso = Describe(exif, Exif, ExifTags.IsoEquivalent);
        exifInfo.FocalLength = Describe(exif, Exif, ExifTags.FocalLength);
        exifInfo.FocalLength35 = Describe(exif, Exif, ExifTags.FocalLength35);
        exifInfo.ExposureBias = ReadExposureBias(exif);
        exifInfo.ExposureProgram = Describe(exif, Exif, ExifTags.ExposureProgram);
        exifInfo.MeteringMode = Describe(exif, Exif, ExifTags.MeteringMode);
        exifInfo.WhiteBalance = Describe(exif, Exif, ExifTags.WhiteBalanceMode);
        exifInfo.Flash = Describe(exif, Exif, ExifTags.Flash);

        exifInfo.Taken = ReadTaken(exif);

        exifInfo.OrientationValue = ReadOrientation(exif);
        exifInfo.Orientation = DescribeOrientation(exifInfo.OrientationValue);

        var colorSpaceExif = Describe(exif, Exif, ExifTags.ColorSpace);
        exifInfo.ColorSpace = AssignValue(colorSpaceExif, Clean(icc?.ColorSpace), Priority.Low);
        exifInfo.IccProfile = Clean(icc?.Description);

        // Formatted as a pair so the two rows line up in the panel's monospace column.
        (exifInfo.GpsLatitude, exifInfo.GpsLongitude) = GpsCoordinateFormat.Align(
            Describe(exif, Gps, ExifTags.GpsLatitude),
            Describe(exif, Gps, ExifTags.GpsLongitude)
        );

        exifInfo.GpsAltitude = Describe(exif, Gps, ExifTags.GpsAltitude);

        exifInfo.Artist = Describe(exif, Ifd0, ExifTags.Artist);
        exifInfo.Copyright = Describe(exif, Ifd0, ExifTags.Copyright);

        // Compression (0x0103) is an IFD0 tag - that is where TIFF puts it. JPEG and PNG fill it
        // in from their own directories later, so this is the TIFF path in practice.
        exifInfo.Compression = FirstNonEmpty(
            Describe(exif, Ifd0, ExifTags.Compression),
            Describe(exif, Exif, ExifTags.Compression)
        );

        exifInfo.Software = Describe(exif, Ifd0, ExifTags.Software);

        return exifInfo;
    }

    private static string ReadTaken(ExifMetadata? exif)
    {
        if (exif is not null
            && (exif.TryGetDateTime(Exif, ExifTags.DateTimeOriginal, out var taken)
                || exif.TryGetDateTime(Exif, ExifTags.DateTimeDigitized, out taken)
                || exif.TryGetDateTime(Ifd0, ExifTags.DateTime, out taken)))
        {
            return taken.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
        }

        return Describe(exif, Exif, ExifTags.DateTimeOriginal);
    }

    private static string ReadExposureBias(ExifMetadata? exif)
    {
        if (exif is null || !exif.TryGetRational(Exif, ExifTags.ExposureBias, out var bias) || bias.Numerator == 0)
            return string.Empty;

        return Describe(exif, Exif, ExifTags.ExposureBias);
    }

    /// <summary>
    /// Reads IFD0 tag 0x0112. Anything outside the 1-8 range defined by the spec is treated as
    /// absent rather than trusted - a wrong rotation is worse than none.
    /// </summary>
    private static ExifOrientation ReadOrientation(ExifMetadata? exif)
    {
        if (exif is null || !exif.TryGetInt32(Ifd0, ExifTags.Orientation, out var raw))
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
