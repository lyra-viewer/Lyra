using System.ComponentModel;

namespace Lyra.Imaging.Content;

public class ExifInfo
{
    public static readonly ExifInfo Error = new();
    
    public string Make = string.Empty;
    public string Model = string.Empty;
    public string Lens = string.Empty;

    [EmptyLine]
    [Description("Exposure Time")]
    public string ExposureTime = string.Empty;

    [Description("Aperture")]
    public string FNumber = string.Empty;

    [Description("ISO")]
    public string Iso = string.Empty;
    
    [Description("Flash")]
    public string Flash = string.Empty;

    [EmptyLine]
    public string Taken = string.Empty;

    [EmptyLine]
    [Description("ICC Profile")]
    public string IccProfile = string.Empty;
    [Description("Color Space")]
    public string ColorSpace = string.Empty;
    [Description("Bits Per Sample")]
    public string ColorDepth = string.Empty;
    [Description("Color Type")]
    public string ColorType = string.Empty;
    
    [EmptyLine]
    [Description("GPS Latitude")]
    public string GpsLatitude = string.Empty;

    [Description("GPS Longitude")]
    public string GpsLongitude = string.Empty;

    [EmptyLine]
    public string Compression = string.Empty;

    public string Software = string.Empty;

    public bool IsValid()
    {
        return this != Error;
    }
    
    public bool HasData()
    {
        var fields = typeof(ExifInfo).GetFields();
        var info = this;
        return fields.Any(field => 
            field.GetValue(info) is string value && !string.IsNullOrWhiteSpace(value));
    }
    
    public List<string> ToLines()
    {
        var lines = new List<string>();
        var fields = typeof(ExifInfo).GetFields();

        foreach (var field in fields)
        {
            if (field.IsDefined(typeof(EmptyLineAttribute), false))
                lines.Add(string.Empty);
            
            var value = field.GetValue(this) as string;
            if (!string.IsNullOrWhiteSpace(value))
            {
                var description = field.GetCustomAttributes(typeof(DescriptionAttribute), false)
                    .OfType<DescriptionAttribute>()
                    .FirstOrDefault()?.Description ?? field.Name;

                lines.Add($"{description}: {value}");
            }
        }

        return PostProcessLines(lines);
    }

    private List<string> PostProcessLines(List<string> lines)
    {
        var cleanedLines = new List<string>();
        var lastWasEmpty = false;

        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                if (lastWasEmpty) 
                    continue; // Allow one empty line
                
                cleanedLines.Add(string.Empty);
                lastWasEmpty = true;
            }
            else
            {
                cleanedLines.Add(line);
                lastWasEmpty = false;
            }
        }

        // Remove leading/trailing empty lines
        while (cleanedLines.Count > 0 && string.IsNullOrWhiteSpace(cleanedLines[0]))
            cleanedLines.RemoveAt(0);

        while (cleanedLines.Count > 0 && string.IsNullOrWhiteSpace(cleanedLines[^1]))
            cleanedLines.RemoveAt(cleanedLines.Count - 1);

        return cleanedLines;
    }
}

[AttributeUsage(AttributeTargets.Field)]
public class EmptyLineAttribute : Attribute;