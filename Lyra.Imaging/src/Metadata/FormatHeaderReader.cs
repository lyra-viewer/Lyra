using System.Globalization;
using Lyra.Imaging.Content.Exif;
using Lyra.Metadata.Containers;
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
    public static void Apply(IReadOnlyList<IFormatFacts> facts, ExifInfo exifInfo)
    {
        foreach (var fact in facts)
        {
            switch (fact)
            {
                case JpegFacts jpeg:
                    ApplyJpeg(jpeg, exifInfo);
                    break;

                case PngFacts png:
                    ApplyPng(png, exifInfo);
                    break;

                case HeifFacts heif:
                    ApplyHeif(heif, exifInfo);
                    break;

                case BmpFacts bmp:
                    foreach (var header in bmp.Headers)
                        ApplyBmp(header, exifInfo);
                    break;

                case IcoFacts ico:
                    ApplyIco(ico, exifInfo);
                    break;

                case TgaFacts tga:
                    exifInfo.ColorDepth = AssignValue(exifInfo.ColorDepth, tga.ImageDepth.ToString(CultureInfo.InvariantCulture), Priority.Low);
                    break;
            }
        }

        // WebP has no facts worth a row - dimensions, alpha and animation are true of almost every
        // file - and PSD's header is described by PsdDecoder through Lyra.Psd.
    }
    
    private static void ApplyJpeg(JpegFacts jpeg, ExifInfo exifInfo)
    {
        var compression = JpegProcesses.GetValueOrDefault(jpeg.FrameMarker, string.Empty);
        exifInfo.Compression = AssignValue(exifInfo.Compression, compression, Priority.Low);
        exifInfo.ColorDepth = AssignValue(exifInfo.ColorDepth, $"{jpeg.Precision} bits", Priority.Low);
        exifInfo.ColorType = AssignValue(exifInfo.ColorType, DescribeJpegColorType(jpeg.Components), Priority.Low);
    }

    private static void ApplyPng(PngFacts png, ExifInfo exifInfo)
    {
        if (png.IccName is { } name)
            exifInfo.IccProfile = AssignValue(exifInfo.IccProfile, Clean(name), Priority.Low);

        exifInfo.ColorDepth = AssignValue(exifInfo.ColorDepth, png.BitDepth.ToString(CultureInfo.InvariantCulture), Priority.High);
        exifInfo.ColorType = AssignValue(exifInfo.ColorType, PngColorTypes.GetValueOrDefault(png.ColorType, string.Empty), Priority.High);
        exifInfo.Compression = AssignValue(exifInfo.Compression, png.CompressionMethod == 0 ? "Deflate" : string.Empty, Priority.Low);
    }

    private static void ApplyBmp(BmpHeader bmp, ExifInfo exifInfo)
    {
        exifInfo.ColorDepth = AssignValue(exifInfo.ColorDepth, Clean(bmp.BitsPerPixel?.ToString(CultureInfo.InvariantCulture)), Priority.Low);
        exifInfo.Compression = AssignValue(exifInfo.Compression, Clean(DescribeBmpCompression(bmp)), Priority.Low);
    }

    /// <summary>A BMP's compression, worded as the panel has always shown it; OS/2's 64-byte header reuses two codes.</summary>
    private static string? DescribeBmpCompression(BmpHeader bmp) => bmp is { Compression: { } compression, HeaderSize: { } size }
        ? compression switch
        {
            0 => "None",
            1 => "RLE 8-bit/pixel",
            2 => "RLE 4-bit/pixel",
            3 => size == 64 ? "Huffman 1D" : "Bit Fields",
            4 => size == 64 ? "RLE 24-bit/pixel" : "JPEG",
            5 => "PNG",
            6 => "RGBA Bit Fields",
            11 => "CMYK Uncompressed",
            12 => "CMYK RLE-8",
            13 => "CMYK RLE-4",
            _ => $"Unimplemented compression type {compression}"
        }
        : null;

    /// <summary>An icon shows its largest entry first, so that entry's depth is the one given.</summary>
    private static void ApplyIco(IcoFacts ico, ExifInfo exifInfo)
    {
        var depth = Clean(ico.Largest?.BitsPerPixel?.ToString(CultureInfo.InvariantCulture));

        if (depth.Length > 0)
            exifInfo.ColorDepth = AssignValue(exifInfo.ColorDepth, depth, Priority.Low);
    }

    private static void ApplyHeif(HeifFacts heif, ExifInfo exifInfo)
    {
        if (heif.BitDepthLuma is { } luma)
            exifInfo.ColorDepth = AssignValue(exifInfo.ColorDepth, luma.ToString(CultureInfo.InvariantCulture), Priority.Low);

        if (exifInfo.ContainerRotation == ExifOrientation.Unknown && heif.Rotation is { } degrees)
            exifInfo.ContainerRotation = ContainerRotation(degrees);
    }

    /// <summary>IHDR's color types, worded as the panel has always shown them.</summary>
    private static readonly Dictionary<int, string> PngColorTypes = new()
    {
        [0] = "Greyscale",
        [2] = "True Color",
        [3] = "Indexed Color",
        [4] = "Greyscale with Alpha",
        [6] = "True Color with Alpha"
    };

    /// <summary>The coding process each SOF marker stands for, worded as the panel has always shown it.</summary>
    private static readonly Dictionary<byte, string> JpegProcesses = new()
    {
        [0xC0] = "Baseline",
        [0xC1] = "Extended sequential, Huffman",
        [0xC2] = "Progressive, Huffman",
        [0xC3] = "Lossless, Huffman",
        [0xC5] = "Differential sequential, Huffman",
        [0xC6] = "Differential progressive, Huffman",
        [0xC7] = "Differential lossless, Huffman",
        [0xC9] = "Extended sequential, arithmetic",
        [0xCA] = "Progressive, arithmetic",
        [0xCB] = "Lossless, arithmetic",
        [0xCD] = "Differential sequential, arithmetic",
        [0xCE] = "Differential progressive, arithmetic",
        [0xCF] = "Differential lossless, arithmetic"
    };

    private static string DescribeJpegColorType(int components) => components switch
    {
        1 => "Grayscale",
        3 => "YCbCr",
        4 => "CMYK / YCCK",
        _ => string.Empty
    };
    
    /// <summary>irot turns anticlockwise; the orientation names the clockwise turn that shows the image upright.</summary>
    private static ExifOrientation ContainerRotation(int degrees) => degrees switch
    {
        0 => ExifOrientation.Normal,
        90 => ExifOrientation.Rotate270Cw,
        180 => ExifOrientation.Rotate180,
        270 => ExifOrientation.Rotate90Cw,
        _ => ExifOrientation.Unknown
    };
}