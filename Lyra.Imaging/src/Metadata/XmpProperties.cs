namespace Lyra.Imaging.Metadata;

/// <summary>
/// Reads values out of MetadataExtractor's flattened XMP view, which hands back property paths
/// rather than a tree:
///
///   dc:title[1]              = "Sunset"
///   dc:title[1]/xml:lang     = "x-default"      (a qualifier, not a value)
///   dc:subject[1]            = "leaf"
///   dc:subject[2]            = "nature"
///   xmp:Rating               = "3"
///
/// Simple properties appear under their bare name, arrays under a 1-based index. Real files are
/// inconsistent about case ("dc:format" and "dc:Format" both occur), so lookups ignore it.
/// </summary>
internal static class XmpProperties
{
    /// <summary>The first value of a property, or empty when it is absent.</summary>
    public static string First(IDictionary<string, string> properties, string name)
    {
        var values = All(properties, name);
        return values.Count > 0 ? values[0] : string.Empty;
    }

    /// <summary>Every value of a property, ordered by array index.</summary>
    public static List<string> All(IDictionary<string, string> properties, string name)
    {
        var values = new List<(int Index, string Value)>();

        foreach (var (key, value) in properties)
        {
            // A qualifier ("dc:title[1]/xml:lang") describes a value rather than being one.
            if (key.Contains('/') || string.IsNullOrWhiteSpace(value))
                continue;

            if (key.Equals(name, StringComparison.OrdinalIgnoreCase))
            {
                values.Add((0, value.Trim()));
                continue;
            }

            if (!key.StartsWith(name + "[", StringComparison.OrdinalIgnoreCase) || !key.EndsWith(']'))
                continue;

            // Indices are ordered numerically, not as text - "[10]" sorts before "[2]" otherwise.
            var index = key[(name.Length + 1)..^1];
            values.Add((int.TryParse(index, out var parsed) ? parsed : 0, value.Trim()));
        }

        return values.OrderBy(entry => entry.Index).Select(entry => entry.Value).ToList();
    }
}