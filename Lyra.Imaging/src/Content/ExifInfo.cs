using System.ComponentModel;
using System.Reflection;

namespace Lyra.Imaging.Content;

public sealed class ExifInfo
{
    public string Title { get; internal set; } = string.Empty;
    public string Description { get; internal set; } = string.Empty;
    public string Keywords { get; internal set; } = string.Empty;
    public string Rating { get; internal set; } = string.Empty;

    [EmptyLine]
    public string Make { get; internal set; } = string.Empty;

    public string Model { get; internal set; } = string.Empty;
    public string Lens { get; internal set; } = string.Empty;

    [EmptyLine]
    [Description("Exposure Time")]
    public string ExposureTime { get; internal set; } = string.Empty;

    [Description("Aperture")]
    public string FNumber { get; internal set; } = string.Empty;

    [Description("ISO")]
    public string Iso { get; internal set; } = string.Empty;

    [Description("Focal Length")]
    public string FocalLength { get; internal set; } = string.Empty;

    [Description("Focal Length (35mm)")]
    public string FocalLength35 { get; internal set; } = string.Empty;

    [Description("Exposure Bias")]
    public string ExposureBias { get; internal set; } = string.Empty;

    [Description("Exposure Program")]
    public string ExposureProgram { get; internal set; } = string.Empty;

    [Description("Metering Mode")]
    public string MeteringMode { get; internal set; } = string.Empty;

    [Description("White Balance")]
    public string WhiteBalance { get; internal set; } = string.Empty;

    [Description("Flash")]
    public string Flash { get; internal set; } = string.Empty;

    [EmptyLine]
    public string Taken { get; internal set; } = string.Empty;

    [EmptyLine]
    public string Orientation { get; internal set; } = string.Empty;

    [Description("ICC Profile")]
    public string IccProfile { get; internal set; } = string.Empty;

    [Description("Color Space")]
    public string ColorSpace { get; internal set; } = string.Empty;

    [Description("Bits Per Sample")]
    public string ColorDepth { get; internal set; } = string.Empty;

    [Description("Color Type")]
    public string ColorType { get; internal set; } = string.Empty;

    [EmptyLine]
    [Description("GPS Latitude")]
    public string GpsLatitude { get; internal set; } = string.Empty;

    [Description("GPS Longitude")]
    public string GpsLongitude { get; internal set; } = string.Empty;

    [Description("GPS Altitude")]
    public string GpsAltitude { get; internal set; } = string.Empty;

    [EmptyLine]
    public string Artist { get; internal set; } = string.Empty;

    public string Copyright { get; internal set; } = string.Empty;

    [EmptyLine]
    public string Compression { get; internal set; } = string.Empty;

    public string Software { get; internal set; } = string.Empty;

    [ExcludeFromSchema]
    public ExifStatus Status { get; internal set; } = ExifStatus.Ok;
    
    [ExcludeFromSchema]
    public ExifOrientation OrientationValue { get; internal set; } = ExifOrientation.Unknown;
    
    [ExcludeFromSchema]
    public ExifOrientation ContainerRotation { get; internal set; } = ExifOrientation.Unknown;

    // ------------------------------------------------------------------
    //  Schema
    // ------------------------------------------------------------------
    
    private static readonly ExifField[] Schema = BuildSchema();

    private static ExifField[] BuildSchema() =>
        typeof(ExifInfo)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(property => !property.IsDefined(typeof(ExcludeFromSchemaAttribute), false))
            .OrderBy(property => property.MetadataToken)
            .Select(CreateField)
            .ToArray();

    private static ExifField CreateField(PropertyInfo property)
    {
        if (property.PropertyType != typeof(string))
            throw new InvalidOperationException(
                $"{nameof(ExifInfo)}.{property.Name} is {property.PropertyType.Name}, but panel rows are strings. "
                + "Mark it [ExcludeFromSchema] if it is not meant to be a row.");

        return new ExifField(
            property.GetCustomAttribute<DescriptionAttribute>()?.Description ?? property.Name,
            property.IsDefined(typeof(EmptyLineAttribute), false),
            property);
    }

    private sealed record ExifField(string Key, bool StartsGroup, PropertyInfo Property)
    {
        public string Read(ExifInfo info) => Property.GetValue(info) as string ?? string.Empty;
    }

    // ------------------------------------------------------------------
    //  Rows
    // ------------------------------------------------------------------

    private IReadOnlyList<ExifEntry>? _entries;

    /// <summary>A record standing in for metadata that could not be read.</summary>
    internal static ExifInfo Failed() => new ExifInfo { Status = ExifStatus.Failed }.Seal();

    /// <summary>
    /// Freezes the record for publication by building the row list up front, on the thread that
    /// populated it. Downstream readers - the UI thread among them - then only ever read.
    /// </summary>
    internal ExifInfo Seal()
    {
        _entries = BuildEntries();
        return this;
    }

    public bool IsValid()
    {
        return Status == ExifStatus.Ok;
    }

    public bool HasData()
    {
        return ToKeyValuePairs().Count > 0;
    }
    
    public IReadOnlyList<ExifEntry> ToKeyValuePairs()
    {
        return _entries ??= BuildEntries();
    }

    public List<string> ToLines()
    {
        return ToKeyValuePairs()
            .Select(entry => entry.IsSeparator ? string.Empty : $"{entry.Key}: {entry.Value}")
            .ToList();
    }

    private IReadOnlyList<ExifEntry> BuildEntries()
    {
        var entries = new List<ExifEntry>(Schema.Length);

        foreach (var field in Schema)
        {
            if (field.StartsGroup)
                entries.Add(ExifEntry.Separator);

            var value = field.Read(this);
            if (!string.IsNullOrWhiteSpace(value))
                entries.Add(new ExifEntry(field.Key, value));
        }

        return CollapseSeparators(entries);
    }

    private static IReadOnlyList<ExifEntry> CollapseSeparators(List<ExifEntry> entries)
    {
        var cleaned = new List<ExifEntry>(entries.Count);
        var lastWasSeparator = false;

        foreach (var entry in entries)
        {
            if (entry.IsSeparator)
            {
                if (lastWasSeparator) continue;
                cleaned.Add(ExifEntry.Separator);
                lastWasSeparator = true;
            }
            else
            {
                cleaned.Add(entry);
                lastWasSeparator = false;
            }
        }

        while (cleaned.Count > 0 && cleaned[0].IsSeparator)
            cleaned.RemoveAt(0);

        while (cleaned.Count > 0 && cleaned[^1].IsSeparator)
            cleaned.RemoveAt(cleaned.Count - 1);

        return cleaned;
    }
}

public record ExifEntry(string Key, string Value)
{
    public static readonly ExifEntry Separator = new(string.Empty, string.Empty);
    public bool IsSeparator => this == Separator;
}

public enum ExifStatus
{
    Ok,
    Failed
}

[AttributeUsage(AttributeTargets.Property)]
public class EmptyLineAttribute : Attribute;

[AttributeUsage(AttributeTargets.Property)]
public class ExcludeFromSchemaAttribute : Attribute;