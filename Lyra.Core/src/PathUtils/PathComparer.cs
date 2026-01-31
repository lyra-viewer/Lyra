namespace Lyra.PathUtils;

public static class PathComparer
{
    public static readonly StringComparer CommonPathComparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
    
    public static bool Equals(string? a, string? b) => CommonPathComparer.Equals(a, b);

    public static int Compare(string? a, string? b) => CommonPathComparer.Compare(a, b);

    public static int GetHashCode(string s) => CommonPathComparer.GetHashCode(s);
}