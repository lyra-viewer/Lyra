using MetadataExtractor;
using MetadataExtractor.Formats.Exif;
using MetadataExtractor.Formats.Jpeg;
using SkiaSharp;

namespace Lyra.Imaging.Decoding.Decoders.Raw;

/// <summary>
/// The color space of an embedded preview that carries no ICC profile, from what the file says
/// instead. Taking such a preview as sRGB shows a Display P3 one, as every iPhone ProRAW has,
/// visibly less saturated than it is.
/// </summary>
internal static class RawPreviewColor
{
    // DNG PreviewColorSpace values.
    private const int DngSrgb = 2;
    private const int DngAdobeRgb = 3;

    private const ushort ExifUncalibrated = 0xFFFF;

    /// <summary>The DCF Interoperability index of an Adobe RGB file.</summary>
    private const string InteropAdobeRgb = "R03";

    private static readonly SKColorSpace Srgb = SKColorSpace.CreateSrgb();
    private static readonly SKColorSpace AdobeRgb = SKColorSpace.CreateRgb(SKColorSpaceTransferFn.TwoDotTwo, SKColorSpaceXyz.AdobeRgb);
    private static readonly SKColorSpace DisplayP3 = SKColorSpace.CreateRgb(SKColorSpaceTransferFn.Srgb, SKColorSpaceXyz.DisplayP3);

    /// <summary>
    /// The DNG's own statement first; then the EXIF conventions: "uncalibrated" with the Adobe RGB
    /// interoperability index is Adobe RGB, and from Apple it is Display P3. sRGB otherwise.
    /// </summary>
    public static SKColorSpace Of(Stream jpeg, int dngPreviewColorSpace)
    {
        switch (dngPreviewColorSpace)
        {
            case DngSrgb:
                return Srgb;
            case DngAdobeRgb:
                return AdobeRgb;
        }

        try
        {
            var directories = JpegMetadataReader.ReadMetadata(jpeg, [new ExifReader()]);
            var colorSpace = directories.OfType<ExifSubIfdDirectory>().FirstOrDefault() is { } exif && exif.TryGetUInt16(ExifDirectoryBase.TagColorSpace, out var value)
                ? value
                : (ushort)0;

            if (colorSpace != ExifUncalibrated)
                return Srgb;

            if (directories.OfType<ExifInteropDirectory>().FirstOrDefault()?.GetString(ExifDirectoryBase.TagInteropIndex) == InteropAdobeRgb)
                return AdobeRgb;

            if (directories.OfType<ExifIfd0Directory>().FirstOrDefault()?.GetString(ExifDirectoryBase.TagMake)?.Trim() == "Apple")
                return DisplayP3;
        }
        catch (Exception e) when (e is JpegProcessingException or IOException or MetadataException) { }

        return Srgb;
    }
}