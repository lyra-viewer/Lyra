using System.Reflection;

namespace Lyra.Common;

public static class ImageFormat
{
    [Flags]
    private enum FormatTraits
    {
        None = 0,
        Disabled = 1 << 0,
        PreloadDisabled = 1 << 1,
        NoPerceptualHash = 1 << 2
    }

    private readonly record struct FormatInfo(ImageFormatType Format, FormatTraits Traits)
    {
        public static readonly FormatInfo Unknown = new(ImageFormatType.Unknown, FormatTraits.None);

        public bool Has(FormatTraits trait) => (Traits & trait) != 0;
    }

    private static readonly Dictionary<string, FormatInfo> InfoByExtension;
    private static readonly int MaxExtensionLength;

    static ImageFormat()
    {
        var formats = Enum.GetValues<ImageFormatType>();
        var traits = formats.ToDictionary(format => format, ReadTraits);

        InfoByExtension = BuildExtensionMap(formats, traits);
        MaxExtensionLength = InfoByExtension.Count == 0 ? 0 : InfoByExtension.Keys.Max(e => e.Length);

        LogDisabledFormats(formats, traits);
    }

    public static ImageFormatType GetImageFormat(string extension) => Lookup(extension).Format;

    public static bool IsSupported(string extension) => IsSupported(Lookup(extension));

    public static bool IsPreloadDisabled(string extension) => Lookup(extension).Has(FormatTraits.PreloadDisabled);

    public static bool IsPerceptualHashSupported(string extension)
    {
        var info = Lookup(extension);
        return IsSupported(info) && !info.Has(FormatTraits.NoPerceptualHash);
    }

    private static bool IsSupported(FormatInfo info) => info.Format != ImageFormatType.Unknown && !info.Has(FormatTraits.Disabled);

    private static FormatInfo Lookup(string? extension)
    {
        if (string.IsNullOrWhiteSpace(extension))
            return FormatInfo.Unknown;

        var normalized = extension.Trim();
        if (normalized.Length > MaxExtensionLength)
            return FormatInfo.Unknown;

        if (normalized[0] != '.')
            normalized = '.' + normalized;

        return InfoByExtension.GetValueOrDefault(normalized, FormatInfo.Unknown);
    }

    private static Dictionary<string, FormatInfo> BuildExtensionMap(ImageFormatType[] formats, Dictionary<ImageFormatType, FormatTraits> traits)
    {
        var map = new Dictionary<string, FormatInfo>(StringComparer.OrdinalIgnoreCase);

        foreach (var format in formats)
        foreach (var extension in ReadAttribute<FileExtensionAttribute>(format)?.Extensions ?? [])
        {
            var normalized = NormalizeExtension(extension);
            if (normalized.Length == 0)
                continue;

            if (map.TryGetValue(normalized, out var existing))
                throw new InvalidOperationException(
                    $"ImageFormatType declares extension {normalized} on both {existing.Format} and {format}. " +
                    $"Each extension must map to exactly one format."
                );

            map[normalized] = new FormatInfo(format, traits[format]);
        }

        return map;
    }

    private static void LogDisabledFormats(ImageFormatType[] formats, Dictionary<ImageFormatType, FormatTraits> traits)
    {
        foreach (var format in formats.Where(f => traits[f].HasFlag(FormatTraits.Disabled)))
            Logger.Warning($"[ImageFormat] {format} is disabled.");

        foreach (var format in formats.Where(f => traits[f].HasFlag(FormatTraits.PreloadDisabled)))
            Logger.Warning($"[ImageFormat] {format} preload is disabled.");
    }

    private static string NormalizeExtension(string extension)
    {
        if (string.IsNullOrWhiteSpace(extension))
            return string.Empty;

        extension = extension.Trim();
        return extension.StartsWith('.') ? extension : '.' + extension;
    }

    private static FormatTraits ReadTraits(ImageFormatType format)
    {
        var traits = FormatTraits.None;

        if (ReadAttribute<DisabledTypeAttribute>(format) is not null)
            traits |= FormatTraits.Disabled;

        if (ReadAttribute<DisabledPreloadAttribute>(format) is not null)
            traits |= FormatTraits.PreloadDisabled;

        if (ReadAttribute<NoPerceptualHashAttribute>(format) is not null)
            traits |= FormatTraits.NoPerceptualHash;

        return traits;
    }

    private static T? ReadAttribute<T>(ImageFormatType format) where T : Attribute =>
        typeof(ImageFormatType).GetMember(format.ToString()).FirstOrDefault()?.GetCustomAttribute<T>();
}