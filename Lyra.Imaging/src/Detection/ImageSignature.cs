using System.Buffers.Binary;
using Lyra.Common;
using Lyra.ManagedCodecs.Raster.Tga;

namespace Lyra.Imaging.Detection;

/// <param name="IsConclusive">Whether the bytes could hardly be anything else. A two-byte signature is not.</param>
internal readonly record struct SignatureMatch(ImageFormatType Format, bool IsConclusive);

/// <summary>
/// What an image is, from its first bytes. A MIME type or file name, as a containing file gives
/// them, is only consulted when the bytes carry no signature, e.g. TGA.
/// </summary>
internal static class ImageSignature
{
    public const int HeadBytes = 64;

    private static readonly HashSet<string> HeifBrands = ["heic", "heix", "hevc", "hevx", "heim", "heis", "hevm", "hevs", "mif1", "msf1", "avif", "avis"];

    public static ImageFormatType Detect(ReadOnlySpan<byte> bytes, string? mimeType, string? fileName)
    {
        if (Match(bytes) is { } match)
            return match.Format;

        var hinted = FromMimeType(mimeType) ?? FromFileName(fileName);
        if (hinted is { } format)
            return format;

        return TgaReader.CanDecode(bytes) ? ImageFormatType.Tga : ImageFormatType.Unknown;
    }

    public static SignatureMatch? Match(ReadOnlySpan<byte> b)
    {
        if (b.StartsWith((ReadOnlySpan<byte>)[0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A]))
            return Conclusive(ImageFormatType.Png);

        if (b.StartsWith((ReadOnlySpan<byte>)[0xFF, 0xD8, 0xFF]))
            return Conclusive(ImageFormatType.Jpeg);

        if (b.StartsWith("GIF87a"u8) || b.StartsWith("GIF89a"u8))
            return Conclusive(ImageFormatType.Gif);

        if (b.Length >= 12 && b.StartsWith("RIFF"u8) && b[8..12].SequenceEqual("WEBP"u8))
            return Conclusive(ImageFormatType.Webp);

        if (b.Length >= 12 && b[4..8].SequenceEqual("ftyp"u8))
            return IsoBrand(b);

        if (b.StartsWith((ReadOnlySpan<byte>)[0xAB, (byte)'K', (byte)'T', (byte)'X', (byte)' ']))
            return Conclusive(ImageFormatType.Ktx);

        if (b.StartsWith("DDS "u8))
            return Conclusive(ImageFormatType.Dds);

        if (b.StartsWith("qoif"u8))
            return Conclusive(ImageFormatType.Qoi);

        if (b.StartsWith("#?RADIANCE"u8) || b.StartsWith("#?RGBE"u8))
            return Conclusive(ImageFormatType.Hdr);

        if (b.StartsWith("8BPS"u8))
            return Conclusive(b.Length >= 6 && BinaryPrimitives.ReadUInt16BigEndian(b[4..]) == 2 ? ImageFormatType.Psb : ImageFormatType.Psd);

        if (b.StartsWith((ReadOnlySpan<byte>)[0x76, 0x2F, 0x31, 0x01]))
            return Conclusive(ImageFormatType.Exr);

        if (b.StartsWith((ReadOnlySpan<byte>)[0xFF, 0x0A]) || b.StartsWith((ReadOnlySpan<byte>)[0, 0, 0, 0x0C, (byte)'J', (byte)'X', (byte)'L', (byte)' ']))
            return Conclusive(ImageFormatType.Jxl);

        if (b.StartsWith((ReadOnlySpan<byte>)[0, 0, 0, 0x0C, (byte)'j', (byte)'P', (byte)' ', (byte)' ']))
            return Conclusive(ImageFormatType.Jp2);

        if (b.StartsWith((ReadOnlySpan<byte>)[0xFF, 0x4F, 0xFF, 0x51]))
            return Conclusive(ImageFormatType.J2k);

        if (b.StartsWith("icns"u8))
            return Conclusive(ImageFormatType.Icns);

        if (b.StartsWith("FUJIFILMCCD-RAW"u8) || b.StartsWith("IIRO"u8) || b.StartsWith("IIRS"u8) || b.StartsWith("MMOR"u8) || b.StartsWith("IIU\0"u8))
            return Conclusive(ImageFormatType.Raw);

        if (b.StartsWith("II*\0"u8) || b.StartsWith("MM\0*"u8) || b.StartsWith("II+\0"u8) || b.StartsWith("MM\0+"u8))
            return Conclusive(ImageFormatType.Tiff);

        if (b.StartsWith("glTF"u8))
            return Conclusive(ImageFormatType.Gltf);

        if (b.StartsWith("Kaydara FBX Binary"u8))
            return Conclusive(ImageFormatType.Fbx);

        if (b.StartsWith("#?"u8))
            return Inconclusive(ImageFormatType.Hdr);

        if (b.StartsWith("BM"u8))
            return Inconclusive(ImageFormatType.Bmp);

        if (b.StartsWith((ReadOnlySpan<byte>)[0, 0, 1, 0]))
            return Inconclusive(ImageFormatType.Ico);

        return null;
    }

    /// <summary>
    /// An ISO base media file is HEIF or AVIF only by its brand: MP4 and MOV video share the box,
    /// and a Canon CR3 raw is one too.
    /// </summary>
    private static SignatureMatch? IsoBrand(ReadOnlySpan<byte> b)
    {
        var boxEnd = (int)Math.Min(BinaryPrimitives.ReadUInt32BigEndian(b), (uint)b.Length);

        for (var at = 8; at + 4 <= boxEnd; at += at == 8 ? 8 : 4)
        {
            var brand = System.Text.Encoding.ASCII.GetString(b.Slice(at, 4));

            if (HeifBrands.Contains(brand))
                return Conclusive(ImageFormatType.Heif);

            if (brand == "crx ")
                return Conclusive(ImageFormatType.Raw);
        }

        return null;
    }

    private static SignatureMatch Conclusive(ImageFormatType format) => new(format, true);

    private static SignatureMatch Inconclusive(ImageFormatType format) => new(format, false);

    private static ImageFormatType? FromMimeType(string? mimeType) => mimeType?.Trim().ToLowerInvariant() switch
    {
        "image/png" => ImageFormatType.Png,
        "image/jpeg" or "image/jpg" => ImageFormatType.Jpeg,
        "image/webp" => ImageFormatType.Webp,
        "image/gif" => ImageFormatType.Gif,
        "image/bmp" => ImageFormatType.Bmp,
        "image/ktx2" or "image/ktx" => ImageFormatType.Ktx,
        "image/vnd-ms.dds" or "image/vnd.ms-dds" or "image/dds" => ImageFormatType.Dds,
        "image/x-tga" or "image/tga" or "image/x-targa" or "image/targa" => ImageFormatType.Tga,
        "image/avif" or "image/heic" or "image/heif" => ImageFormatType.Heif,
        _ => null
    };

    private static ImageFormatType? FromFileName(string? fileName)
    {
        if (string.IsNullOrEmpty(fileName))
            return null;

        var cut = fileName.LastIndexOfAny(['/', '\\']);
        var format = ImageFormat.GetImageFormat(Path.GetExtension(fileName[(cut + 1)..]));
        return format == ImageFormatType.Unknown ? null : format;
    }
}