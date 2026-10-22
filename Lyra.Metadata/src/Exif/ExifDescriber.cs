using System.Globalization;
using Lyra.Metadata.Tiff;

namespace Lyra.Metadata.Exif;

internal static partial class ExifDescriber
{
    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

    public static string? Describe(ExifMetadata metadata, TiffDirectoryKind kind, ushort tag)
    {
        if (metadata.Get(kind, tag) is not { } value)
            return null;

        if (kind == TiffDirectoryKind.Gps)
            return DescribeGps(metadata, tag, value);

        return tag switch
        {
            ExifTags.ExposureTime => value.TryGetRational(out var time) ? SimpleString(time) + " sec" : null,
            ExifTags.FNumber => value.TryGetRational(out var f) ? "f/" + Math.Round(ToDouble(f), 1, MidpointRounding.AwayFromZero).ToString("0.0", Invariant) : null,
            ExifTags.FocalLength => value.TryGetRational(out var mm) ? ToDouble(mm).ToString("0.#", Invariant) + " mm" : null,
            ExifTags.FocalLength35 => value.TryGetInt32(out var mm35) ? mm35 == 0 ? "Unknown" : mm35.ToString(Invariant) + " mm" : null,
            ExifTags.ExposureBias => value.TryGetRational(out var bias) ? SimpleString(bias) + " EV" : null,
            ExifTags.IsoEquivalent => value.TryGetInt32(out var iso) ? iso.ToString(Invariant) : null,
            ExifTags.ExposureProgram => Indexed(value, ExposurePrograms),
            ExifTags.MeteringMode => Indexed(value, MeteringModes),
            ExifTags.WhiteBalanceMode => Indexed(value, WhiteBalanceModes),
            ExifTags.ColorSpace => Indexed(value, ColorSpaces),
            ExifTags.Compression => Indexed(value, CompressionNames),
            ExifTags.Flash => value.TryGetInt32(out var flash) ? FlashNames[flash & 0x7F] : null,
            _ => Plain(value)
        };
    }

    private static string? DescribeGps(ExifMetadata metadata, ushort tag, ExifValue value)
    {
        switch (tag)
        {
            case ExifTags.GpsLatitude or ExifTags.GpsLongitude:
                if (GeoLocation(metadata) is not { } location)
                    return null;

                return DegreesMinutesSeconds(tag == ExifTags.GpsLatitude ? location.Latitude : location.Longitude);

            case ExifTags.GpsAltitude:
                return value.TryGetRational(out var altitude) ? ToDouble(altitude).ToString("0.##", Invariant) + " metres" : null;

            default:
                return Plain(value);
        }
    }

    private readonly record struct Location(double Latitude, double Longitude);

    /// <summary>Both coordinates and both references, or nothing: the position is not half-known.</summary>
    private static Location? GeoLocation(ExifMetadata metadata)
    {
        const TiffDirectoryKind gps = TiffDirectoryKind.Gps;
        if (metadata.Get(gps, ExifTags.GpsLatitude)?.Rationals is not { Length: 3 } latitude
            || metadata.Get(gps, ExifTags.GpsLongitude)?.Rationals is not { Length: 3 } longitude
            || metadata.Get(gps, ExifTags.GpsLatitudeRef)?.Text is not { } latitudeRef
            || metadata.Get(gps, ExifTags.GpsLongitudeRef)?.Text is not { } longitudeRef)
        {
            return null;
        }

        var lat = ToDecimal(latitude, latitudeRef.Equals("S", StringComparison.OrdinalIgnoreCase));
        var lon = ToDecimal(longitude, longitudeRef.Equals("W", StringComparison.OrdinalIgnoreCase));

        return lat is { } a && lon is { } b ? new Location(a, b) : null;
    }

    private static double? ToDecimal(TiffRational[] dms, bool negative)
    {
        var value = Math.Abs(ToDouble(dms[0])) + ToDouble(dms[1]) / 60.0 + ToDouble(dms[2]) / 3600.0;
        if (double.IsNaN(value))
            return null;

        return negative ? -value : value;
    }

    private static string DegreesMinutesSeconds(double value)
    {
        var degrees = (int)value;
        var minutes = Math.Abs(value % 1 * 60);
        var seconds = minutes % 1 * 60;

        return $"{degrees.ToString("0.##", Invariant)}° {((int)minutes).ToString("0.##", Invariant)}' {seconds.ToString("0.##", Invariant)}\"";
    }

    private static string? Indexed(ExifValue value, IReadOnlyDictionary<int, string> names)
    {
        if (!value.TryGetInt32(out var index))
            return null;

        return names.TryGetValue(index, out var name) ? name : $"Unknown ({index.ToString(Invariant)})";
    }

    private static string? Plain(ExifValue value) => value.Text ?? (value.TryGetInt32(out var number) ? number.ToString(Invariant) : null);

    private static double ToDouble(TiffRational value) => value.Numerator == 0 ? 0 : (double)value.Numerator / value.Denominator;
    
    internal static string SimpleString(TiffRational value)
    {
        var (numerator, denominator) = value;

        if (denominator == 0 && numerator != 0)
            return $"{numerator.ToString(Invariant)}/{denominator.ToString(Invariant)}";

        if (denominator == 1 || (denominator != 0 && numerator % denominator == 0) || (denominator == 0 && numerator == 0))
            return (denominator == 0 ? 0 : numerator / denominator).ToString(Invariant);

        if (numerator != 1 && denominator % numerator == 0)
            return SimpleString(new TiffRational(1, denominator / numerator));

        var simplified = Simplify(value);
        var asDecimal = ToDouble(simplified).ToString(Invariant);
        if (asDecimal.Length < 5)
            return asDecimal;

        return $"{simplified.Numerator.ToString(Invariant)}/{simplified.Denominator.ToString(Invariant)}";
    }

    private static TiffRational Simplify(TiffRational value)
    {
        var (numerator, denominator) = value;
        if (denominator < 0)
        {
            numerator = -numerator;
            denominator = -denominator;
        }

        var gcd = Gcd(Math.Abs(numerator), denominator);
        return gcd == 0 ? new TiffRational(numerator, denominator) : new TiffRational(numerator / gcd, denominator / gcd);
    }

    private static long Gcd(long a, long b)
    {
        while (b != 0)
            (a, b) = (b, a % b);

        return a;
    }
}