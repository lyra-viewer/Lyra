using System.Globalization;

namespace Lyra;

/// <summary>
/// Picks the newest of a set of versioned shared libraries - <c>libheif.so.1</c>,
/// <c>libheif.so.1.19.8</c>, <c>libSDL3.so.0.3200.0</c> and so on.
/// </summary>
internal static class SonameVersion
{
    /// <summary>
    /// The highest-versioned candidate, or null when none of them is a versioned form of
    /// <paramref name="libraryName"/>.
    /// </summary>
    /// <param name="candidates">Paths, as a directory listing yields them.</param>
    /// <param name="libraryName">The unversioned name, e.g. <c>libheif.so</c>.</param>
    public static string? SelectHighest(IEnumerable<string> candidates, string libraryName)
    {
        string? best = null;
        int[]? bestVersion = null;

        foreach (var candidate in candidates)
        {
            if (VersionOf(Path.GetFileName(candidate), libraryName) is not { } version)
                continue;

            if (bestVersion is not null && Compare(version, bestVersion) <= 0)
                continue;

            best = candidate;
            bestVersion = version;
        }

        return best;
    }

    /// <summary>
    /// The version <paramref name="fileName"/> carries, or null when it is not
    /// <paramref name="libraryName"/> followed by one.
    /// </summary>
    private static int[]? VersionOf(string fileName, string libraryName)
    {
        var prefix = libraryName + ".";

        if (!fileName.StartsWith(prefix, StringComparison.Ordinal))
            return null;

        var suffix = fileName[prefix.Length..];
        if (suffix.Length == 0)
            return null;

        var parts = suffix.Split('.');
        var version = new int[parts.Length];

        for (var i = 0; i < parts.Length; i++)
            if (!int.TryParse(parts[i], NumberStyles.None, CultureInfo.InvariantCulture, out version[i]))
                return null;

        return version;
    }

    /// <summary>
    /// Compares component by component, a missing component counting as lower - so 1.19.8 comes
    /// out above 1, and 10 above 2.
    /// </summary>
    private static int Compare(int[] left, int[] right)
    {
        for (var i = 0; i < Math.Max(left.Length, right.Length); i++)
        {
            var l = i < left.Length ? left[i] : -1;
            var r = i < right.Length ? right[i] : -1;

            if (l != r)
                return l.CompareTo(r);
        }

        return 0;
    }
}