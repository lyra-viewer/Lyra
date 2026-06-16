using System.Reflection;

namespace Lyra.Common;

public static class ImageFormat
{
    static ImageFormat()
    {
        foreach (var format in Enum.GetValues<ImageFormatType>())
        {
            if (HasAttribute<DisabledTypeAttribute>(format))
                Logger.Warning($"[ImageFormat] {format} is disabled.");
        }

        foreach (var format in Enum.GetValues<ImageFormatType>())
        {
            if (HasAttribute<DisabledPreloadAttribute>(format))
                Logger.Warning($"[ImageFormat] {format} preload is disabled.");
        }
    }

    public static ImageFormatType GetImageFormat(string extension)
    {
        var normalized = NormalizeExtension(extension);
        return GetImageFormatInternal(normalized);
    }

    public static bool IsSupported(string extension)
    {
        var normalized = NormalizeExtension(extension);

        var format = GetImageFormatInternal(normalized);
        if (format == ImageFormatType.Unknown)
            return false;

        return !HasAttribute<DisabledTypeAttribute>(format);
    }

    public static bool IsPreloadDisabled(string extension)
    {
        var normalized = NormalizeExtension(extension);

        var format = GetImageFormatInternal(normalized);
        return HasAttribute<DisabledPreloadAttribute>(format);
    }
    
    public static bool IsPerceptualHashSupported(string extension)
    {
        var normalized = NormalizeExtension(extension);

        var format = GetImageFormatInternal(normalized);
        if (format == ImageFormatType.Unknown || HasAttribute<DisabledTypeAttribute>(format))
            return false;

        return !HasAttribute<NoPerceptualHashAttribute>(format);
    }

    private static ImageFormatType GetImageFormatInternal(string normalizedExtension)
    {
        foreach (var format in Enum.GetValues<ImageFormatType>())
        {
            var attr = GetAttribute<FileExtensionAttribute>(format);
            if (attr != null && attr.Extensions.Contains(normalizedExtension, StringComparer.OrdinalIgnoreCase))
                return format;
        }

        return ImageFormatType.Unknown;
    }

    private static string NormalizeExtension(string extension)
    {
        if (string.IsNullOrWhiteSpace(extension))
            return string.Empty;

        extension = extension.Trim().ToLowerInvariant();
        return extension.StartsWith('.') ? extension : '.' + extension;
    }

    private static T? GetAttribute<T>(ImageFormatType format) where T : Attribute
    {
        var memberInfo = typeof(ImageFormatType).GetMember(format.ToString()).FirstOrDefault();
        return memberInfo?.GetCustomAttribute<T>();
    }

    private static bool HasAttribute<T>(ImageFormatType format) where T : Attribute
    {
        return GetAttribute<T>(format) != null;
    }
}