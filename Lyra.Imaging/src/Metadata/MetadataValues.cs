using Lyra.Common;
using Lyra.Metadata.Exif;
using Lyra.Metadata.Tiff;

namespace Lyra.Imaging.Metadata;

/// <summary>
/// The normalization layer between Lyra.Metadata's readings and the panel.
///
/// Every value that reaches ExifInfo goes through <see cref="Describe"/>, so "absent" has exactly
/// one representation and the merge rules in <see cref="AssignValue"/> can rely on it.
/// </summary>
internal static class MetadataValues
{
    /// <summary>Reads a tag as display text, with placeholder values normalized away.</summary>
    public static string Describe(ExifMetadata? exif, TiffDirectoryKind kind, ushort tag) => Clean(exif?.Describe(kind, tag));
    
    public static string Clean(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var trimmed = value.Trim();

        // "Unknown (8)": the placeholder word plus the raw value it could not name. Strip the
        // parenthesized part before matching, but match on the whole remainder - a value that
        // merely starts with one of these words ("Unknown artist") is real text.
        var candidate = trimmed;
        if (trimmed.EndsWith(')'))
        {
            var open = trimmed.LastIndexOf('(');
            if (open > 0)
                candidate = trimmed[..open].TrimEnd();
        }

        return candidate.ToLowerInvariant() switch
        {
            "undefined" or "unknown" or "reserved" => string.Empty,
            "n/a" or "-" => string.Empty,
            _ => trimmed
        };
    }

    public static string AssignValue(string currentValue, string newValue, Priority newValuePriority)
    {
        if (string.IsNullOrEmpty(currentValue))
            return newValue;

        if (newValue.Equals(currentValue) || string.IsNullOrWhiteSpace(newValue))
            return currentValue;

        if (newValuePriority == Priority.High)
        {
            Logger.Warning($"[MetadataValues] Value \"{currentValue}\" replaced by \"{newValue}\"");
            return newValue;
        }

        return currentValue;
    }

    public static string FirstNonEmpty(params string[] candidates) =>
        candidates.FirstOrDefault(candidate => !string.IsNullOrWhiteSpace(candidate)) ?? string.Empty;

    public static string Truncate(string value, int maxLength)
    {
        if (value.Length <= maxLength)
            return value;

        var cut = maxLength;
        if (cut > 0 && char.IsHighSurrogate(value[cut - 1]))
            cut--;

        return value[..cut].TrimEnd() + "…";
    }
}

internal enum Priority
{
    Low,
    High
}