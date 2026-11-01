using Lyra.Common;

namespace Lyra.Imaging.Detection;

/// <param name="Named">What the file's extension says it is.</param>
/// <param name="Format">What it is taken to be, which differs only when its bytes clearly disagree.</param>
public readonly record struct FormatDetection(ImageFormatType Named, ImageFormatType Format)
{
    public bool IsMisnamed => Format != Named;
}

public static class FormatDetector
{
    public static FormatDetection Detect(string path) => Detect(path, out _);

    /// <param name="head">The file's first bytes as read, or null when it could not be read.</param>
    internal static FormatDetection Detect(string path, out byte[]? head)
    {
        var named = ImageFormat.GetImageFormat(Path.GetExtension(path));
        var buffer = new byte[ImageSignature.HeadBytes];

        if (ReadHead(path, buffer) is not { } read)
        {
            head = null;
            return new FormatDetection(named, named);
        }

        head = read == buffer.Length ? buffer : buffer[..read];
        return new FormatDetection(named, Resolve(named, head));
    }
    
    public static ImageFormatType? Identify(string path)
    {
        Span<byte> head = stackalloc byte[ImageSignature.HeadBytes];

        return ReadHead(path, head) is { } read
               && ImageSignature.Match(head[..read]) is { IsConclusive: true } match
               && ImageFormat.IsSupported(match.Format)
            ? match.Format
            : null;
    }

    private static int? ReadHead(string path, Span<byte> head)
    {
        try
        {
            using var stream = File.OpenRead(path);
            return stream.ReadAtLeast(head, head.Length, throwOnEndOfStream: false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
    
    internal static ImageFormatType Resolve(ImageFormatType named, ReadOnlySpan<byte> head)
    {
        if (named == ImageFormatType.Unknown)
            return ImageSignature.Detect(head, mimeType: null, fileName: null);

        return ImageSignature.Match(head) is { IsConclusive: true } match && !Agrees(named, match.Format)
            ? match.Format
            : named;
    }

    /// <summary>Whether the decoder the name picks reads what the bytes say, so the name can stand.</summary>
    private static bool Agrees(ImageFormatType named, ImageFormatType detected) => named == detected || named switch
    {
        ImageFormatType.Jpeg or ImageFormatType.Jfif => detected is ImageFormatType.Jpeg or ImageFormatType.Jfif,
        ImageFormatType.Psd or ImageFormatType.Psb => detected is ImageFormatType.Psd or ImageFormatType.Psb,
        ImageFormatType.Jp2 or ImageFormatType.J2k => detected is ImageFormatType.Jp2 or ImageFormatType.J2k,

        // Many raws are TIFF inside, and some begin with their JPEG preview.
        ImageFormatType.Raw => detected is ImageFormatType.Tiff or ImageFormatType.Jpeg,

        // The icon decoder shows a plain PNG or BMP itself.
        ImageFormatType.Ico => detected is ImageFormatType.Png or ImageFormatType.Bmp,

        _ => false
    };
}