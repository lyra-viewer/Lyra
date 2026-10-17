using System.Buffers.Binary;
using System.Globalization;
using Lyra.Imaging.Content;
using Lyra.Imaging.Interop;
using static Lyra.Imaging.Metadata.MetadataValues;

namespace Lyra.Imaging.Metadata;

/// <summary>Capture facts LibRaw parsed itself, for raws whose EXIF is missing or unreadable (CRW).</summary>
internal readonly record struct RawCapture(
    string Make,
    string Model,
    string Lens,
    float Iso,
    float Shutter,
    float Aperture,
    float FocalLength,
    DateTime? Taken)
{
    public static RawCapture From(in RawNative.RawInfo info) => new(
        info.MakeText,
        info.ModelText,
        info.LensText,
        info.IsoSpeed,
        info.Shutter,
        info.Aperture,
        info.FocalLength,
        info.Timestamp > 0 ? DateTimeOffset.FromUnixTimeSeconds(info.Timestamp).LocalDateTime : null);
}

internal static class RawMetadata
{
    private const int MaxTtwBytes = 16 << 20;

    /// <summary>The TIFF in an MRW's TTW block, which MetadataExtractor cannot find itself; null for anything else.</summary>
    public static byte[]? MrwTiff(Stream stream)
    {
        if (!stream.CanSeek)
            return null;

        var start = stream.Position;
        try
        {
            Span<byte> header = stackalloc byte[8];
            if (!ReadAt(stream, start, header) || !header[..4].SequenceEqual("\0MRM"u8))
                return null;

            var end = Math.Min(start + 8 + BinaryPrimitives.ReadUInt32BigEndian(header[4..]), stream.Length);
            var offset = start + 8;

            for (var i = 0; offset + 8 <= end && i < 64; i++)
            {
                if (!ReadAt(stream, offset, header))
                    return null;

                var length = BinaryPrimitives.ReadUInt32BigEndian(header[4..]);
                if (header[..4].SequenceEqual("\0TTW"u8))
                {
                    if (length is 0 or > MaxTtwBytes || offset + 8 + length > stream.Length)
                        return null;

                    var tiff = new byte[length];
                    stream.Position = offset + 8;
                    stream.ReadExactly(tiff);
                    return tiff;
                }

                offset += 8L + length;
            }

            return null;
        }
        finally
        {
            stream.Position = start;
        }
    }

    private static bool ReadAt(Stream stream, long offset, Span<byte> buffer)
    {
        if (offset < 0 || offset + buffer.Length > stream.Length)
            return false;

        stream.Position = offset;
        stream.ReadExactly(buffer);
        return true;
    }

    public static void Apply(ExifInfo exifInfo, RawCapture capture)
    {
        // These describe the embedded thumbnail or preview (IFD0, a RAF's JPEG), not the raw data.
        exifInfo.Compression = string.Empty;
        exifInfo.ColorDepth = string.Empty;
        exifInfo.ColorType = string.Empty;

        exifInfo.Make = AssignValue(exifInfo.Make, capture.Make, Priority.Low);
        exifInfo.Model = AssignValue(exifInfo.Model, capture.Model, Priority.Low);
        exifInfo.Lens = AssignValue(exifInfo.Lens, capture.Lens, Priority.Low);
        exifInfo.Iso = AssignValue(exifInfo.Iso, Format(capture.Iso, "0"), Priority.Low);
        exifInfo.ExposureTime = AssignValue(exifInfo.ExposureTime, DescribeShutter(capture.Shutter), Priority.Low);
        exifInfo.FNumber = AssignValue(exifInfo.FNumber, capture.Aperture > 0 ? $"f/{Format(capture.Aperture, "0.0")}" : string.Empty, Priority.Low);
        exifInfo.FocalLength = AssignValue(exifInfo.FocalLength, capture.FocalLength > 0 ? $"{Format(capture.FocalLength, "0.#")} mm" : string.Empty, Priority.Low);

        if (capture.Taken is { } taken)
            exifInfo.Taken = AssignValue(exifInfo.Taken, taken.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture), Priority.Low);
    }

    // Third-stop speeds; LibRaw's APEX conversion lands just off them (1/501).
    private static readonly int[] StandardDenominators =
        [8000, 6400, 5000, 4000, 3200, 2500, 2000, 1600, 1250, 1000, 800, 640, 500, 400, 320, 250, 200, 160, 125, 100, 80, 60, 50, 40, 30, 25, 20, 15, 13, 10, 8, 6, 5, 4];

    internal static string DescribeShutter(float seconds)
    {
        if (!(seconds > 0) || !float.IsFinite(seconds))
            return string.Empty;

        if (seconds > 0.26f)
            return $"{Format(seconds, "0.#")} sec";

        var denominator = 1 / seconds;
        var nearest = StandardDenominators.MinBy(d => Math.Abs(d - denominator));
        var shown = Math.Abs(nearest - denominator) <= nearest * 0.02f ? nearest : MathF.Round(denominator);

        return $"1/{shown.ToString(CultureInfo.InvariantCulture)} sec";
    }

    private static string Format(float value, string format) => value > 0 && float.IsFinite(value) ? value.ToString(format, CultureInfo.InvariantCulture) : string.Empty;
}