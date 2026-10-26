using Lyra.Metadata;
using Lyra.Metadata.Containers;
using Lyra.Metadata.Exif;
using Lyra.Metadata.Tiff;
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
    public static SKColorSpace Of(ReadOnlySpan<byte> jpeg, int dngPreviewColorSpace)
    {
        switch (dngPreviewColorSpace)
        {
            case DngSrgb:
                return Srgb;
            case DngAdobeRgb:
                return AdobeRgb;
        }

        if (Exif(jpeg) is not { } exif)
            return Srgb;

        var colorSpace = exif.TryGetInt32(TiffDirectoryKind.Exif, ExifTags.ColorSpace, out var value) ? value : 0;
        if (colorSpace != ExifUncalibrated)
            return Srgb;

        if (exif.Get(TiffDirectoryKind.Interop, ExifTags.InteropIndex)?.Text == InteropAdobeRgb)
            return AdobeRgb;

        if (exif.Get(TiffDirectoryKind.Image, ExifTags.Make)?.Text?.Trim() == "Apple")
            return DisplayP3;

        return Srgb;
    }

    private static ExifMetadata? Exif(ReadOnlySpan<byte> jpeg) => JpegContainer.Read(jpeg) is { } container ? ImageMetadata.From(container).Exif : null;
}