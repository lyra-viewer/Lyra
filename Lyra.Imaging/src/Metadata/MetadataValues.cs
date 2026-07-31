using System.Globalization;
using Lyra.Common;
using Directory = MetadataExtractor.Directory;

namespace Lyra.Imaging.Metadata;

/// <summary>
/// The normalization layer between MetadataExtractor and the panel.
///
/// Every value that reaches ExifInfo goes through <see cref="Describe"/>, so "absent" has exactly
/// one representation and the merge rules in <see cref="AssignValue"/> can rely on it.
/// </summary>
internal static class MetadataValues
{
    /// <summary>Reads a tag as display text, with placeholder values normalized away.</summary>
    public static string Describe(Directory? directory, int tag) => Clean(directory?.GetDescription(tag));

    /// <summary>
    /// MetadataExtractor renders unset or unrecognized tags as words rather than as nothing:
    /// "Undefined", "Unknown", "Unknown (8)". Apple writes ColorSpace = Undefined on virtually
    /// every HEIC and Display-P3 JPEG, and left alone that placeholder both occupies a row and
    /// outranks the real value from the ICC profile, because AssignValue only treats empty as
    /// absent. Values are also trimmed - ICC returns four-character codes padded to width.
    /// </summary>
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

    /// <summary>
    /// Extracts the readable profile name from the ICC 'desc' tag.
    ///
    /// Modern profiles store it as multiLocalizedUnicode, which MetadataExtractor renders as
    /// "&lt;count&gt; &lt;locale&gt;(&lt;text&gt;)" - e.g. "1 enUS(Display P3)" - so the panel
    /// would otherwise show the record structure instead of the name. Older profiles use a plain
    /// text description and arrive ready to display.
    /// </summary>
    public static string ExtractIccProfileName(string value)
    {
        var firstSpace = value.IndexOf(' ');
        if (firstSpace <= 0 || !int.TryParse(value.AsSpan(0, firstSpace), NumberStyles.Integer, CultureInfo.InvariantCulture, out var count))
            return value;

        var open = value.IndexOf('(', firstSpace);
        if (open < 0)
            return value;

        // With a single record the name itself may contain parentheses, so the last ')' closes
        // it. With several, stop at the first one and show the first locale.
        var close = count == 1 ? value.LastIndexOf(')') : value.IndexOf(')', open + 1);
        if (close <= open + 1)
            return value;

        var name = value[(open + 1)..close].Trim();
        return name.Length > 0 ? name : value;
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