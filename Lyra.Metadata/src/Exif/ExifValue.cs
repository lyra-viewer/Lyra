using System.Globalization;
using Lyra.Metadata.Tiff;

namespace Lyra.Metadata.Exif;

/// <summary>A tag's value, decoded by its TIFF type. Exactly one of the arrays or the text is set.</summary>
public sealed class ExifValue
{
    public long[]? Integers { get; init; }
    public TiffRational[]? Rationals { get; init; }
    public string? Text { get; init; }
    public byte[]? Bytes { get; init; }

    /// <summary>A single whole number: an integer, a whole rational, or text that parses as one.</summary>
    public bool TryGetInt32(out int value)
    {
        value = 0;

        if (Integers is [var integer] && integer is >= int.MinValue and <= int.MaxValue)
        {
            value = (int)integer;
            return true;
        }

        if (Rationals is [{ IsDefined: true } rational] && rational.Numerator % rational.Denominator == 0)
        {
            var whole = rational.Numerator / rational.Denominator;
            if (whole is >= int.MinValue and <= int.MaxValue)
            {
                value = (int)whole;
                return true;
            }
        }

        return Text is { } text && int.TryParse(text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
    }

    public bool TryGetRational(out TiffRational value)
    {
        if (Rationals is [var rational])
        {
            value = rational;
            return true;
        }

        if (Integers is [var integer])
        {
            value = new TiffRational(integer, 1);
            return true;
        }

        value = default;
        return false;
    }

    internal static ExifValue? Read(Stream stream, TiffEntry entry, bool little, int maxCount, int maxBytes) => entry.Type switch
    {
        TiffFieldType.Ascii => TiffDirectoryReader.ReadText(stream, entry, maxBytes) is { } text ? new ExifValue { Text = text } : null,
        TiffFieldType.Undefined => TiffDirectoryReader.ReadBytes(stream, entry, maxBytes) is { } bytes ? new ExifValue { Bytes = bytes } : null,
        TiffFieldType.Rational or TiffFieldType.SRational => TiffDirectoryReader.ReadRationals(stream, entry, little, maxCount) is { } rationals ? new ExifValue { Rationals = rationals } : null,
        TiffFieldType.Float or TiffFieldType.Double => null,
        _ => TiffDirectoryReader.ReadIntegers(stream, entry, little, maxCount) is { } integers ? new ExifValue { Integers = integers } : null
    };
}