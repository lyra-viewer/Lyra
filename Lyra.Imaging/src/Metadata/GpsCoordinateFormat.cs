namespace Lyra.Imaging.Metadata;

internal static class GpsCoordinateFormat
{
    private const int MinuteWidth = 2;
    private const int SecondWidth = 2;

    /// <summary>
    /// Aligns two coordinates against each other. Either may be empty - a file can record one
    /// without the other - in which case the present one is still padded on its own terms.
    /// </summary>
    public static (string Latitude, string Longitude) Align(string latitude, string longitude)
    {
        if (!TryParse(latitude, out var lat) || !TryParse(longitude, out var lon))
            return (Align(latitude), Align(longitude));

        var degreeWidth = Math.Max(Width(lat.Degrees), Width(lon.Degrees));

        return (Format(lat, degreeWidth), Format(lon, degreeWidth));
    }

    /// <summary>Aligns a single coordinate, leaving its degrees at their natural width.</summary>
    private static string Align(string coordinate) =>
        TryParse(coordinate, out var parts) ? Format(parts, Width(parts.Degrees)) : coordinate;

    private static string Format(Parts parts, int degreeWidth) =>
        Pad(parts.Degrees, degreeWidth) + "° "
        + Pad(parts.Minutes, MinuteWidth) + "' "
        + Pad(parts.Seconds, SecondWidth) + '"'
        + parts.Trailing;
    
    private static bool TryParse(string coordinate, out Parts parts)
    {
        parts = default;

        var degreeMark = coordinate.IndexOf('°');
        if (degreeMark < 0)
            return false;

        var minuteMark = coordinate.IndexOf('\'', degreeMark + 1);
        if (minuteMark < 0)
            return false;

        var secondMark = coordinate.IndexOf('"', minuteMark + 1);
        if (secondMark < 0)
            return false;

        parts = new Parts(
            coordinate[..degreeMark].Trim(),
            coordinate[(degreeMark + 1)..minuteMark].Trim(),
            coordinate[(minuteMark + 1)..secondMark].Trim(),
            coordinate[(secondMark + 1)..]);

        return true;
    }

    private static int Width(string value)
    {
        var index = value.Length > 0 && value[0] is '-' or '+' ? 1 : 0;

        while (index < value.Length && char.IsDigit(value[index]))
            index++;

        return index;
    }

    private static string Pad(string value, int width)
    {
        var padding = width - Width(value);
        return padding > 0 ? new string(' ', padding) + value : value;
    }

    private readonly record struct Parts(string Degrees, string Minutes, string Seconds, string Trailing);
}