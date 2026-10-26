using System.Globalization;
using Lyra.Metadata.Exif;
using Lyra.Metadata.Tiff;
using Xunit;
using static Lyra.Metadata.Tests.Support.Golden;

namespace Lyra.Metadata.Tests.Exif;

/// <summary>The wording of every tag the panel shows, for values across each tag's range, as recorded in Golden/.</summary>
public class ExifDescriberGoldenTests
{
    private static ExifMetadata Single(TiffDirectoryKind kind, ushort tag, ExifValue value) =>
        new(new Dictionary<TiffDirectoryKind, IReadOnlyDictionary<ushort, ExifValue>> { [kind] = new Dictionary<ushort, ExifValue> { [tag] = value } });

    private static string Describe(TiffDirectoryKind kind, ushort tag, ExifValue value) => Cell(Single(kind, tag, value).Describe(kind, tag));

    private static IEnumerable<TiffRational> Rationals()
    {
        for (long numerator = -8; numerator <= 48; numerator++)
        for (long denominator = -2; denominator <= 24; denominator++)
            yield return new TiffRational(numerator, denominator);

        long[] large = [1, 3, 7, 10, 13, 25, 333, 1000, 1249, 2500, 4000, 8000, 12500, 30000, 65535, 1_000_000];
        foreach (var numerator in large)
        foreach (var denominator in large)
            yield return new TiffRational(numerator, denominator);
    }

    [Fact]
    public void RationalsAreWordedAsRecorded()
    {
        var lines = Rationals().Select(value =>
        {
            var exif = new ExifValue { Rationals = [value] };
            var unsigned = value is { Numerator: >= 0, Denominator: >= 0 };
            string Unsigned(TiffDirectoryKind kind, ushort tag) => unsigned ? Describe(kind, tag, exif) : "-";

            return string.Join('\t',
                $"{value.Numerator}/{value.Denominator}",
                Unsigned(TiffDirectoryKind.Exif, ExifTags.ExposureTime),
                Unsigned(TiffDirectoryKind.Exif, ExifTags.FNumber),
                Unsigned(TiffDirectoryKind.Exif, ExifTags.FocalLength),
                Describe(TiffDirectoryKind.Exif, ExifTags.ExposureBias, exif),
                Unsigned(TiffDirectoryKind.Gps, ExifTags.GpsAltitude));
        });

        Verify("exif-rationals", lines);
    }

    [Fact]
    public void IndexedTagsAreWordedAsRecorded()
    {
        ushort[] tags =
        [
            ExifTags.FocalLength35, ExifTags.IsoEquivalent, ExifTags.ExposureProgram, ExifTags.MeteringMode,
            ExifTags.WhiteBalanceMode, ExifTags.ColorSpace, ExifTags.Compression, ExifTags.Flash
        ];

        long[] values = [.. Enumerable.Range(0, 301).Select(value => (long)value), 1000, 6400, 32766, 32767, 32769, 32773, 34712, 34892, 65000, 65534, 65535, 70000];

        var lines = values.Select(value =>
            string.Join('\t', [value.ToString(CultureInfo.InvariantCulture), .. tags.Select(tag => Describe(TiffDirectoryKind.Exif, tag, new ExifValue { Integers = [value] }))]));

        Verify("exif-indexed", lines);
    }

    [Fact]
    public void DatesParseAsRecorded()
    {
        string[] dates =
        [
            "2024:09:26 18:32:17", "2024:09:26 18:32", "2024-09-26 18:32:17", "2024-09-26T18:32:17", "2024:09:26",
            "    :  :     :  :  ", "0000:00:00 00:00:00", "2024:13:40 99:99:99", "2024:09:26 18:32:17.123",
            "2024.09.26 18:32:17", "2024:9:6 8:2:7", "2024", "20240926", " 2024:09:26 18:32:17", "2024:09:26 18:32:17 ",
            "2024-09-26T18:32:17.5", "2024-09"
        ];

        var lines = dates.Select(date =>
        {
            var exif = Single(TiffDirectoryKind.Exif, ExifTags.DateTimeOriginal, new ExifValue { Text = date });
            var parsed = exif.TryGetDateTime(TiffDirectoryKind.Exif, ExifTags.DateTimeOriginal, out var value)
                ? value.ToString("yyyy-MM-ddTHH:mm:ss.fff", CultureInfo.InvariantCulture)
                : "-";

            return $"{Cell(date)}\t{parsed}";
        });

        Verify("exif-dates", lines);
    }

    [Fact]
    public void PositionsAreWordedAsRecorded()
    {
        TiffRational[][] coordinates =
        [
            [new(40, 1), new(26, 1), new(463, 10)],
            [new(33, 1), new(51, 1), new(2195, 100)],
            [new(4044, 100), new(0, 1), new(0, 1)],
            [new(0, 1), new(0, 1), new(0, 1)],
            [new(50, 1), new(0, 0), new(0, 1)],
            [new(179, 1), new(59, 1), new(5999, 100)],
            [new(12, 1), new(30, 1)]
        ];
        string?[] latitudeRefs = ["N", "S", "E", "W", "s", "w", null, ""];
        string?[] longitudeRefs = ["E", "W", "w", null];

        var lines = new List<string>();
        foreach (var latitude in coordinates)
        foreach (var longitude in coordinates.Take(3))
        foreach (var latitudeRef in latitudeRefs)
        foreach (var longitudeRef in longitudeRefs)
        {
            var values = new Dictionary<ushort, ExifValue>
            {
                [ExifTags.GpsLatitude] = new() { Rationals = latitude },
                [ExifTags.GpsLongitude] = new() { Rationals = longitude }
            };

            if (latitudeRef is not null)
                values[ExifTags.GpsLatitudeRef] = new ExifValue { Text = latitudeRef };

            if (longitudeRef is not null)
                values[ExifTags.GpsLongitudeRef] = new ExifValue { Text = longitudeRef };

            var exif = new ExifMetadata(new Dictionary<TiffDirectoryKind, IReadOnlyDictionary<ushort, ExifValue>> { [TiffDirectoryKind.Gps] = values });

            lines.Add(string.Join('\t',
                string.Join(' ', latitude.Select(r => $"{r.Numerator}/{r.Denominator}")),
                string.Join(' ', longitude.Select(r => $"{r.Numerator}/{r.Denominator}")),
                Cell(latitudeRef), Cell(longitudeRef),
                Cell(exif.Describe(TiffDirectoryKind.Gps, ExifTags.GpsLatitude)),
                Cell(exif.Describe(TiffDirectoryKind.Gps, ExifTags.GpsLongitude))));
        }

        Verify("exif-gps", lines);
    }

    [Fact]
    public void APositionWithinADegreeWestOrSouthKeepsItsSign()
    {
        var exif = new ExifMetadata(new Dictionary<TiffDirectoryKind, IReadOnlyDictionary<ushort, ExifValue>>
        {
            [TiffDirectoryKind.Gps] = new Dictionary<ushort, ExifValue>
            {
                [ExifTags.GpsLatitude] = new() { Rationals = [new(0, 1), new(30, 1), new(0, 1)] },
                [ExifTags.GpsLatitudeRef] = new() { Text = "S" },
                [ExifTags.GpsLongitude] = new() { Rationals = [new(0, 1), new(7, 1), new(30, 1)] },
                [ExifTags.GpsLongitudeRef] = new() { Text = "W" }
            }
        });

        Assert.Equal("-0° 30' 0\"", exif.Describe(TiffDirectoryKind.Gps, ExifTags.GpsLatitude));
        Assert.Equal("-0° 7' 30\"", exif.Describe(TiffDirectoryKind.Gps, ExifTags.GpsLongitude));
    }

    [Fact]
    public void TextIsShownAsWritten()
    {
        foreach (var tag in new[] { ExifTags.Make, ExifTags.Model, ExifTags.LensModel, ExifTags.Artist, ExifTags.Copyright, ExifTags.Software, ExifTags.DateTimeOriginal })
        foreach (var text in new[] { "Canon", "NIKON CORPORATION", "2024:09:26 18:32:17", "Ünïcødé ©", "" })
            Assert.Equal(text, Single(TiffDirectoryKind.Exif, tag, new ExifValue { Text = text }).Describe(TiffDirectoryKind.Exif, tag));
    }
}