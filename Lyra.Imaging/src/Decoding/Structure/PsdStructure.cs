using Lyra.Common;
using Lyra.Imaging.Content;
using Lyra.Psd;
using Lyra.Psd.Core.Common;
using Lyra.Psd.Core.SectionData;

namespace Lyra.Imaging.Decoding.Structure;

/// <summary>
/// Builds the file-structure inspector model for a PSD/PSB document: the five top-level sections
/// (File Header, Color Mode Data, Image Resources, Layer &amp; Mask Information, Image Data) in their
/// on-disk order, each annotated with its byte offset and length.
///
/// Structural facts (section offsets/lengths) come from the already-parsed
/// <see cref="PsdDocument.SectionLayout"/>; this layer only selects, names, and formats them for
/// display. Per-layer detail is intentionally omitted here; the Layers dropdown renders that.
/// </summary>
internal static class PsdStructure
{
    // Cap how many child rows a single section may emit, so a pathological file with thousands of
    // resource/additional-info blocks can't flood the inspector.
    private const int MaxChildRows = 64;

    public static IReadOnlyList<StructureGroup> Describe(PsdDocument psd)
    {
        ArgumentNullException.ThrowIfNull(psd);

        var layout = psd.SectionLayout;

        var fileHeader = HeaderGroup(psd.FileHeader, layout.FileHeader);
        var colorMode = ColorModeGroup(psd.ColorModeData, psd.FileHeader.ColorMode, layout.ColorModeData);
        var resources = ResourcesGroup(psd.ImageResources, layout.ImageResources);
        var layerMask = LayerMaskGroup(psd.LayerAndMaskInformation, layout.LayerAndMaskInformation);
        var imageData = ImageDataGroup(psd.ImageData, layout.ImageData);

        return [fileHeader, colorMode, resources, layerMask, imageData];
    }

    // --------------------------------------------------------
    //  Section groups
    // --------------------------------------------------------

    private static StructureGroup HeaderGroup(FileHeader h, PsdSectionSpan span) => new()
    {
        Name = "File Header",
        Description = "Core image attributes (8BPS)",
        SizeBytes = span.Length,
        Fields =
        [
            Pair("Offset", Hex(span.Offset)),
            Pair("Signature", "\"8BPS\""),
            Pair("Version", h.Version == 2 ? "2 (PSB)" : "1 (PSD)"),
            Pair("Channels", $"{h.NumberOfChannels}"),
            Pair("Width", $"{h.Width} px"),
            Pair("Height", $"{h.Height} px"),
            Pair("Depth", $"{h.Depth}-bit"),
            Pair("Color Mode", $"{h.ColorMode} ({(int)h.ColorMode})"),
        ],
    };

    private static StructureGroup ColorModeGroup(ColorModeData data, ColorMode mode, PsdSectionSpan span)
    {
        var contents = data.Length switch
        {
            0 => "None",
            _ when mode == ColorMode.Indexed => $"{data.Length / 3} color palette",
            _ when mode == ColorMode.Duotone => "Duotone specification",
            _ => "Raw data",
        };

        return new StructureGroup
        {
            Name = "Color Mode Data",
            Description = "Palette / duotone color table",
            SizeBytes = span.Length,
            Fields =
            [
                Pair("Offset", Hex(span.Offset)),
                Pair("Length", $"{data.Length} bytes"),
                Pair("Contents", contents),
            ],
        };
    }

    private static StructureGroup ResourcesGroup(ImageResources resources, PsdSectionSpan span)
    {
        var blocks = resources.Blocks;

        var fields = new List<KeyValuePair<string, string>>
        {
            Pair("Offset", Hex(span.Offset)),
            Pair("Length", $"{resources.Length} bytes"),
            Pair("Block Count", $"{blocks.Length}"),
        };

        var shown = Math.Min(blocks.Length, MaxChildRows);
        for (var i = 0; i < shown; i++)
        {
            var b = blocks[i];
            fields.Add(Pair(PsdImageResourceNames.GetName(b.Id), Formatters.SizeToStr(b.DataSize)));
        }

        if (blocks.Length > shown)
            fields.Add(Pair("…", $"+{blocks.Length - shown} more blocks"));

        return new StructureGroup
        {
            Name = "Image Resources",
            Description = $"{blocks.Length} metadata resource block(s)",
            SizeBytes = span.Length,
            Fields = fields,
        };
    }

    private static StructureGroup LayerMaskGroup(LayerAndMaskInformation info, PsdSectionSpan span)
    {
        var layerInfo = info.LayerInfo;
        var additional = info.AdditionalInfo;

        var fields = new List<KeyValuePair<string, string>>
        {
            Pair("Offset", Hex(span.Offset)),
            Pair("Section Length", $"{info.SectionLength} bytes"),
            Pair("Layer Count", $"{layerInfo.EffectiveLayerCount}"),
            Pair("Merged Alpha", YesNo(layerInfo.HasMergedAlpha)),
            Pair("Global Mask", info.GlobalLayerMask.PayloadLength > 0 ? Formatters.SizeToStr(info.GlobalLayerMask.PayloadLength) : "None"),
            Pair("Additional Blocks", $"{additional.Length}"),
        };

        var shown = Math.Min(additional.Length, MaxChildRows);
        for (var i = 0; i < shown; i++)
        {
            var a = additional[i];
            fields.Add(Pair($"\"{a.KeyFourCC}\"", Formatters.SizeToStr(a.PayloadLength)));
        }

        if (additional.Length > shown)
            fields.Add(Pair("…", $"+{additional.Length - shown} more blocks"));

        return new StructureGroup
        {
            Name = "Layer & Mask Information",
            Description = "Layers, masks and extra layer data",
            SizeBytes = span.Length,
            Fields = fields,
        };
    }

    private static StructureGroup ImageDataGroup(ImageData data, PsdSectionSpan span) => new()
    {
        Name = "Image Data",
        Description = "Merged composite image",
        SizeBytes = span.Length,
        Fields =
        [
            Pair("Offset", Hex(span.Offset)),
            Pair("Compression", $"{data.CompressionType}"),
            Pair("Payload Size", Formatters.SizeToStr(data.PayloadLength)),
        ],
    };

    // --------------------------------------------------------
    //  Formatting helpers
    // --------------------------------------------------------

    private static KeyValuePair<string, string> Pair(string key, string value) => new(key, value);

    private static string Hex(long value) => $"0x{value:X}";

    private static string YesNo(bool value) => value ? "Yes" : "No";
}