using System.Buffers;
using System.Collections.Frozen;
using System.Text;
using System.Text.Unicode;
using System.Xml;
using System.Xml.Linq;

namespace Lyra.Metadata.Xmp;

public static class XmpNamespaces
{
    public const string Rdf       = "http://www.w3.org/1999/02/22-rdf-syntax-ns#";
    public const string Xml       = "http://www.w3.org/XML/1998/namespace";
    public const string Xmlns     = "http://www.w3.org/2000/xmlns/";
    public const string Dc        = "http://purl.org/dc/elements/1.1/";
    public const string Xmp       = "http://ns.adobe.com/xap/1.0/";
    public const string XmpRights = "http://ns.adobe.com/xap/1.0/rights/";
    public const string Photoshop = "http://ns.adobe.com/photoshop/1.0/";
    public const string Exif      = "http://ns.adobe.com/exif/1.0/";
    public const string ExifEx    = "http://cipa.jp/exif/1.0/";
    public const string Aux       = "http://ns.adobe.com/exif/1.0/aux/";
    public const string Pdf       = "http://ns.adobe.com/pdf/1.3/";
    public const string Tiff      = "http://ns.adobe.com/tiff/1.0/";
    public const string Png       = "http://ns.adobe.com/png/1.0/";
}

public enum XmpArrayKind
{
    None,
    Bag,
    Seq,
    Alt
}

public sealed record XmpItem(string? Value, string? Language, IReadOnlyList<XmpProperty>? Fields = null);

public readonly record struct XmpName(string Namespace, string Name);

public sealed record XmpProperty(string Namespace, string Name, XmpArrayKind Kind, IReadOnlyList<XmpItem> Items);

public sealed class XmpPacket : IMetadataBlock<XmpPacket>
{
    private const int MaxCharacters = 16 * 1024 * 1024;
    private const int MaxProperties = 10_000;
    private const int MaxItems = 10_000;
    private const int MaxStructDepth = 2;

    public IReadOnlyList<XmpProperty> Properties { get; }

    private XmpPacket(IReadOnlyList<XmpProperty> properties) => Properties = properties;

    /// <summary>
    /// The packet, or null when the bytes are not XML that can be read safely. XMP is UTF-8, but
    /// writers slip Windows-1252 bytes in; a packet that fails on them is read again with each
    /// stray byte taken as that character.
    /// </summary>
    public static XmpPacket? Parse(ReadOnlySpan<byte> data)
    {
        if (data.IsEmpty)
            return null;

        return TryParse(data) ?? (IsUtf16(data) || Utf8.IsValid(data) ? null : TryParse(RepairUtf8(data)));
    }

    private static XmpPacket? TryParse(ReadOnlySpan<byte> data)
    {
        var settings = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            MaxCharactersInDocument = MaxCharacters,
            MaxCharactersFromEntities = 0,
            IgnoreComments = true,
            IgnoreProcessingInstructions = true
        };

        try
        {
            using var stream = new MemoryStream(data.ToArray(), writable: false);
            using var reader = XmlReader.Create(stream, settings);
            
            if (reader.MoveToContent() != XmlNodeType.Element)
                return null;

            XElement root;
            using (var subtree = reader.ReadSubtree())
                root = XElement.Load(subtree);

            var properties = new List<XmpProperty>();
            var rdf = root.Name == XName.Get("RDF", XmpNamespaces.Rdf) ? root : root.Descendants(XName.Get("RDF", XmpNamespaces.Rdf)).FirstOrDefault();

            foreach (var description in rdf?.Elements() ?? [])
                ReadDescription(description, properties, depth: 0);

            return new XmpPacket(properties);
        }
        catch (Exception e) when (e is XmlException or InvalidOperationException or ArgumentException)
        {
            return null;
        }
    }

    private static bool IsUtf16(ReadOnlySpan<byte> data) => data is [0xFE, 0xFF, ..] or [0xFF, 0xFE, ..];

    /// <summary>Characters 0x80-0x9F of Windows-1252; Latin-1 has control codes there.</summary>
    private const string Windows1252High =
        "\u20AC\u0081\u201A\u0192\u201E\u2026\u2020\u2021\u02C6\u2030\u0160\u2039\u0152\u008D\u017D\u008F\u0090\u2018\u2019\u201C\u201D\u2022\u2013\u2014\u02DC\u2122\u0161\u203A\u0153\u009D\u017E\u0178";

    private static byte[] RepairUtf8(ReadOnlySpan<byte> data)
    {
        var text = new StringBuilder(data.Length);
        while (!data.IsEmpty)
        {
            if (Rune.DecodeFromUtf8(data, out var rune, out var consumed) == OperationStatus.Done)
            {
                text.Append(rune.ToString());
                data = data[consumed..];
                continue;
            }

            var b = data[0];
            text.Append(b is >= 0x80 and <= 0x9F ? Windows1252High[b - 0x80] : (char)b);
            data = data[1..];
        }

        return Encoding.UTF8.GetBytes(text.ToString());
    }

    /// <summary>
    /// Every value of a property in reading order, the x-default first in a language alternative.
    /// Struct items have no value and are left out.
    /// </summary>
    public IReadOnlyList<string> Values(string ns, string name)
    {
        var aliases = Properties.Select(p => AliasOf(p, ns, name) is { } alias ? new AliasUse(p, alias.Form) : null)
            .OfType<AliasUse>()
            .OrderBy(use => FirstOfNamespace(use.Property.Namespace))
            .ToList();

        if (Find(ns, name) is not { } property)
            return aliases.Count > 0 ? ValuesOf(aliases[0].Property) : [];

        if (property.Kind == XmpArrayKind.Alt && !property.Items.Any(IsDefault) && aliases.FirstOrDefault(a => a.Form == AliasForm.DefaultLanguage)?.Property is { } language)
            return [.. ValuesOf(property), .. ValuesOf(language).Take(1)];

        if (property.Kind is XmpArrayKind.Bag or XmpArrayKind.Seq && property.Items.Count == 0 && aliases.FirstOrDefault(a => a.Form == AliasForm.FirstItem)?.Property is { } item)
            return ValuesOf(item);

        return ValuesOf(property);
    }

    private int FirstOfNamespace(string ns)
    {
        for (var i = 0; i < Properties.Count; i++)
            if (Properties[i].Namespace == ns)
                return i;

        return int.MaxValue;
    }

    private static IReadOnlyList<string> ValuesOf(XmpProperty property)
    {
        var items = property.Kind == XmpArrayKind.Alt
            ? property.Items.Where(IsDefault).Concat(property.Items.Where(item => !IsDefault(item)))
            : property.Items;

        return items.Where(item => item.Value is not null).Select(item => item.Value!).ToList();
    }

    public string? First(string ns, string name) => Values(ns, name) is [var first, ..] ? first : null;

    private XmpProperty? Find(string ns, string name) => Properties.FirstOrDefault(p => p.Namespace == ns && string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));

    private enum AliasForm
    {
        /// <summary>The alias is the whole property.</summary>
        Whole,

        /// <summary>The alias is the first item of an ordered array.</summary>
        FirstItem,

        /// <summary>The alias is the x-default item of a language alternative.</summary>
        DefaultLanguage
    }

    private readonly record struct Alias(XmpName Actual, AliasForm Form);

    /// <summary>A property in the packet that is an alias of the one asked for.</summary>
    private sealed record AliasUse(XmpProperty Property, AliasForm Form);

    /// <summary>
    /// The aliases the XMP specification registers: older or format-specific names for a property.
    /// An alias stands in for the property when it is absent - the first alias in the packet, if
    /// there are several - and also fills its x-default when it has none, or its array when it is empty.
    /// </summary>
    private static readonly FrozenDictionary<XmpName, Alias> Aliases = new Dictionary<XmpName, Alias>
    {
        [new(XmpNamespaces.Xmp, "Author")] = new(new(XmpNamespaces.Dc, "creator"), AliasForm.FirstItem),
        [new(XmpNamespaces.Xmp, "Authors")] = new(new(XmpNamespaces.Dc, "creator"), AliasForm.Whole),
        [new(XmpNamespaces.Xmp, "Description")] = new(new(XmpNamespaces.Dc, "description"), AliasForm.Whole),
        [new(XmpNamespaces.Xmp, "Format")] = new(new(XmpNamespaces.Dc, "format"), AliasForm.Whole),
        [new(XmpNamespaces.Xmp, "Keywords")] = new(new(XmpNamespaces.Dc, "subject"), AliasForm.Whole),
        [new(XmpNamespaces.Xmp, "Locale")] = new(new(XmpNamespaces.Dc, "language"), AliasForm.Whole),
        [new(XmpNamespaces.Xmp, "Title")] = new(new(XmpNamespaces.Dc, "title"), AliasForm.Whole),
        [new(XmpNamespaces.XmpRights, "Copyright")] = new(new(XmpNamespaces.Dc, "rights"), AliasForm.Whole),
        [new(XmpNamespaces.Pdf, "Author")] = new(new(XmpNamespaces.Dc, "creator"), AliasForm.FirstItem),
        [new(XmpNamespaces.Pdf, "BaseURL")] = new(new(XmpNamespaces.Xmp, "BaseURL"), AliasForm.Whole),
        [new(XmpNamespaces.Pdf, "CreationDate")] = new(new(XmpNamespaces.Xmp, "CreateDate"), AliasForm.Whole),
        [new(XmpNamespaces.Pdf, "Creator")] = new(new(XmpNamespaces.Xmp, "CreatorTool"), AliasForm.Whole),
        [new(XmpNamespaces.Pdf, "ModDate")] = new(new(XmpNamespaces.Xmp, "ModifyDate"), AliasForm.Whole),
        [new(XmpNamespaces.Pdf, "Subject")] = new(new(XmpNamespaces.Dc, "description"), AliasForm.DefaultLanguage),
        [new(XmpNamespaces.Pdf, "Title")] = new(new(XmpNamespaces.Dc, "title"), AliasForm.DefaultLanguage),
        [new(XmpNamespaces.Photoshop, "Author")] = new(new(XmpNamespaces.Dc, "creator"), AliasForm.FirstItem),
        [new(XmpNamespaces.Photoshop, "Caption")] = new(new(XmpNamespaces.Dc, "description"), AliasForm.DefaultLanguage),
        [new(XmpNamespaces.Photoshop, "Copyright")] = new(new(XmpNamespaces.Dc, "rights"), AliasForm.DefaultLanguage),
        [new(XmpNamespaces.Photoshop, "Keywords")] = new(new(XmpNamespaces.Dc, "subject"), AliasForm.Whole),
        [new(XmpNamespaces.Photoshop, "Marked")] = new(new(XmpNamespaces.XmpRights, "Marked"), AliasForm.Whole),
        [new(XmpNamespaces.Photoshop, "Title")] = new(new(XmpNamespaces.Dc, "title"), AliasForm.DefaultLanguage),
        [new(XmpNamespaces.Photoshop, "WebStatement")] = new(new(XmpNamespaces.XmpRights, "WebStatement"), AliasForm.Whole),
        [new(XmpNamespaces.Tiff, "Artist")] = new(new(XmpNamespaces.Dc, "creator"), AliasForm.FirstItem),
        [new(XmpNamespaces.Tiff, "Copyright")] = new(new(XmpNamespaces.Dc, "rights"), AliasForm.Whole),
        [new(XmpNamespaces.Tiff, "DateTime")] = new(new(XmpNamespaces.Xmp, "ModifyDate"), AliasForm.Whole),
        [new(XmpNamespaces.Exif, "DateTimeDigitized")] = new(new(XmpNamespaces.Xmp, "CreateDate"), AliasForm.Whole),
        [new(XmpNamespaces.Tiff, "ImageDescription")] = new(new(XmpNamespaces.Dc, "description"), AliasForm.Whole),
        [new(XmpNamespaces.Tiff, "Software")] = new(new(XmpNamespaces.Xmp, "CreatorTool"), AliasForm.Whole),
        [new(XmpNamespaces.Png, "Author")] = new(new(XmpNamespaces.Dc, "creator"), AliasForm.FirstItem),
        [new(XmpNamespaces.Png, "Copyright")] = new(new(XmpNamespaces.Dc, "rights"), AliasForm.DefaultLanguage),
        [new(XmpNamespaces.Png, "CreationTime")] = new(new(XmpNamespaces.Xmp, "CreateDate"), AliasForm.Whole),
        [new(XmpNamespaces.Png, "Description")] = new(new(XmpNamespaces.Dc, "description"), AliasForm.DefaultLanguage),
        [new(XmpNamespaces.Png, "ModificationTime")] = new(new(XmpNamespaces.Xmp, "ModifyDate"), AliasForm.Whole),
        [new(XmpNamespaces.Png, "Software")] = new(new(XmpNamespaces.Xmp, "CreatorTool"), AliasForm.Whole),
        [new(XmpNamespaces.Png, "Title")] = new(new(XmpNamespaces.Dc, "title"), AliasForm.DefaultLanguage)
    }.ToFrozenDictionary();

    private static Alias? AliasOf(XmpProperty property, string ns, string name) =>
        Aliases.TryGetValue(new XmpName(property.Namespace, property.Name), out var alias)
        && alias.Actual.Namespace == ns 
        && string.Equals(alias.Actual.Name, name, StringComparison.OrdinalIgnoreCase)
            ? alias
            : null;
    
    private static bool IsDefault(XmpItem item) => string.Equals(item.Language, "x-default", StringComparison.OrdinalIgnoreCase);

    private static void ReadDescription(XElement description, List<XmpProperty> into, int depth)
    {
        foreach (var attribute in description.Attributes())
        {
            if (into.Count >= MaxProperties)
                return;

            if (IsPropertyName(attribute.Name))
                into.Add(new XmpProperty(attribute.Name.NamespaceName, attribute.Name.LocalName, XmpArrayKind.None, [new XmpItem(attribute.Value, null)]));
        }

        foreach (var element in description.Elements())
        {
            if (into.Count >= MaxProperties)
                return;

            if (IsPropertyName(element.Name) && ReadProperty(element, depth) is { } property)
                into.Add(property);
        }
    }

    private static XmpProperty? ReadProperty(XElement element, int depth)
    {
        var ns = element.Name.NamespaceName;
        var name = element.Name.LocalName;
        var language = (string?)element.Attribute(XName.Get("lang", XmpNamespaces.Xml));

        if (element.Elements().FirstOrDefault() is { } child)
        {
            var kind = child.Name.NamespaceName == XmpNamespaces.Rdf ? child.Name.LocalName switch
            {
                "Bag" => XmpArrayKind.Bag,
                "Seq" => XmpArrayKind.Seq,
                "Alt" => XmpArrayKind.Alt,
                _ => XmpArrayKind.None
            } : XmpArrayKind.None;

            if (kind != XmpArrayKind.None)
            {
                var items = child.Elements(XName.Get("li", XmpNamespaces.Rdf)).Take(MaxItems).Select(li => ReadItem(li, depth)).ToList();
                return new XmpProperty(ns, name, kind, items);
            }

            return new XmpProperty(ns, name, XmpArrayKind.None, [new XmpItem(null, language, ReadStruct(element, depth))]);
        }

        if ((string?)element.Attribute(XName.Get("resource", XmpNamespaces.Rdf)) is { } resource)
            return new XmpProperty(ns, name, XmpArrayKind.None, [new XmpItem(resource, language)]);

        if ((string?)element.Attribute(XName.Get("parseType", XmpNamespaces.Rdf)) == "Resource" || element.Attributes().Any(a => IsPropertyName(a.Name)))
            return new XmpProperty(ns, name, XmpArrayKind.None, [new XmpItem(null, language, ReadStruct(element, depth))]);

        return new XmpProperty(ns, name, XmpArrayKind.None, [new XmpItem(element.Value, language)]);
    }

    private static XmpItem ReadItem(XElement li, int depth)
    {
        var language = (string?)li.Attribute(XName.Get("lang", XmpNamespaces.Xml));

        if (li.HasElements || (string?)li.Attribute(XName.Get("parseType", XmpNamespaces.Rdf)) == "Resource")
            return new XmpItem(null, language, ReadStruct(li, depth));

        if ((string?)li.Attribute(XName.Get("resource", XmpNamespaces.Rdf)) is { } resource)
            return new XmpItem(resource, language);

        return new XmpItem(li.Value, language);
    }

    private static IReadOnlyList<XmpProperty> ReadStruct(XElement owner, int depth)
    {
        if (depth >= MaxStructDepth)
            return [];

        var fields = new List<XmpProperty>();
        var nested = owner.Elements(XName.Get("Description", XmpNamespaces.Rdf)).FirstOrDefault();

        ReadDescription(nested ?? owner, fields, depth + 1);
        return fields;
    }

    /// <summary>A name that is a property, not RDF syntax, an xml: attribute or a namespace binding.</summary>
    private static bool IsPropertyName(XName name) => name.NamespaceName is not (XmpNamespaces.Rdf or XmpNamespaces.Xml or XmpNamespaces.Xmlns or "");
}
