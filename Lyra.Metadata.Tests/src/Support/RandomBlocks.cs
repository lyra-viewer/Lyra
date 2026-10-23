using System.Text;

namespace Lyra.Metadata.Tests.Support;

/// <summary>XMP packets, IPTC blocks and ICC profiles in the shapes writers produce, damaged ones among them.</summary>
internal static class RandomBlocks
{
    public static string Xmp(Random random)
    {
        T Pick<T>(params T[] options) => options[random.Next(options.Length)];

        var dcPrefix = Pick("dc", "d", "dcx");
        var xmpPrefix = Pick("xmp", "xap", "a");
        var ns = $"""xmlns:{dcPrefix}="http://purl.org/dc/elements/1.1/" xmlns:{xmpPrefix}="http://ns.adobe.com/xap/1.0/" """
                 + """xmlns:tiff="http://ns.adobe.com/tiff/1.0/" xmlns:pdf="http://ns.adobe.com/pdf/1.3/" xmlns:photoshop="http://ns.adobe.com/photoshop/1.0/" xmlns:png="http://ns.adobe.com/png/1.0/" xmlns:xmpRights="http://ns.adobe.com/xap/1.0/rights/" xmlns:xmp="http://ns.adobe.com/xap/1.0/" """;

        var attributes = new StringBuilder();
        var elements = new StringBuilder();

        string Li(string value, string? lang = null) => lang is null ? $"<rdf:li>{value}</rdf:li>" : $"<rdf:li xml:lang=\"{lang}\">{value}</rdf:li>";
        string Alt(params string[] items) => $"<rdf:Alt>{string.Concat(items)}</rdf:Alt>";

        if (random.Next(4) != 0)
        {
            elements.Append(Pick(
                $"<{dcPrefix}:title>{Alt(Li("Sunset", "x-default"), Li("Abend", "de"))}</{dcPrefix}:title>",
                $"<{dcPrefix}:title>{Alt(Li("Abend", "de"), Li("Sunset", "x-default"))}</{dcPrefix}:title>",
                $"<{dcPrefix}:title>{Alt(Li("Only German", "de"))}</{dcPrefix}:title>",
                $"<{dcPrefix}:title>Plain title</{dcPrefix}:title>",
                $"<{dcPrefix}:title>{Alt(Li("   ", "x-default"), Li("Second", "en"))}</{dcPrefix}:title>",
                $"<{dcPrefix}:title>{Alt(Li("  Padded  ", "x-default"))}</{dcPrefix}:title>")
            );
        }

        if (random.Next(3) != 0)
            elements.Append($"<{dcPrefix}:description>{Pick(Alt(Li("A description", "x-default")), "Plain description", Alt(Li("Unknown", "x-default")))}</{dcPrefix}:description>");

        if (random.Next(3) != 0)
        {
            var count = random.Next(0, 12);
            var items = string.Concat(Enumerable.Range(0, count).Select(i => Li(Pick($"kw{i}", " ", "a;b", $"Ünïcode {i}"))));
            elements.Append(Pick($"<{dcPrefix}:subject><rdf:Bag>{items}</rdf:Bag></{dcPrefix}:subject>", $"<{dcPrefix}:subject>single</{dcPrefix}:subject>"));
        }

        if (random.Next(3) != 0)
        {
            var rating = Pick("3", "-1", "0", "5", "7", "x", " 4 ");
            if (random.Next(2) == 0)
                attributes.Append($" {xmpPrefix}:Rating=\"{rating}\"");
            else
                elements.Append($"<{xmpPrefix}:Rating>{rating}</{xmpPrefix}:Rating>");
        }

        if (random.Next(3) != 0)
            elements.Append($"<{dcPrefix}:creator><rdf:Seq>{Pick(Li("Jane"), Li(" ") + Li("Second"), Li("Jörg") + Li("Ann"))}</rdf:Seq></{dcPrefix}:creator>");

        if (random.Next(3) != 0)
            elements.Append($"<{dcPrefix}:rights>{Pick(Alt(Li("(c) 2024", "x-default")), "(c) plain")}</{dcPrefix}:rights>");

        if (random.Next(3) == 0)
        {
            var aliases = string.Concat(Enumerable.Range(0, random.Next(1, 4)).Select(_ => Pick(
                "<tiff:Artist>Ta</tiff:Artist>", "<pdf:Author>Pa</pdf:Author>", "<photoshop:Author>Psa</photoshop:Author>",
                "<photoshop:Caption>Cap</photoshop:Caption>", "<photoshop:Title>Pst</photoshop:Title>", "<photoshop:Keywords>pk</photoshop:Keywords>",
                "<photoshop:Copyright>Psc</photoshop:Copyright>", "<tiff:ImageDescription>Tid</tiff:ImageDescription>", "<xmp:Keywords>xk</xmp:Keywords>",
                "<xmpRights:Copyright>Rc</xmpRights:Copyright>", "<pdf:Title>Pdt</pdf:Title>", "<png:Author>Pnga</png:Author>", "<pdf:Keywords>notalias</pdf:Keywords>")
            ).Distinct());
            elements.Append(aliases);
        }

        if (random.Next(4) == 0)
            elements.Append($"""<{xmpPrefix}:Thumbnails rdf:parseType="Resource"><{xmpPrefix}:Format>JPEG</{xmpPrefix}:Format></{xmpPrefix}:Thumbnails>""");

        var description = random.Next(3) == 0
            ? $"<rdf:Description rdf:about=\"\" {ns}{attributes}/><rdf:Description rdf:about=\"\" {ns}>{elements}</rdf:Description>"
            : $"<rdf:Description rdf:about=\"\" {ns}{attributes}>{elements}</rdf:Description>";

        var rdf = $"<rdf:RDF xmlns:rdf=\"http://www.w3.org/1999/02/22-rdf-syntax-ns#\">{description}</rdf:RDF>";

        return random.Next(6) switch
        {
            0 => rdf,
            1 => "<x:xmpmeta><rdf:RDF",
            2 => $"<?xpacket begin=\"﻿\" id=\"W5M0MpCehiHzreSzNTczkc9d\"?><x:xmpmeta xmlns:x=\"adobe:ns:meta/\">{rdf}</x:xmpmeta>{new string(' ', 200)}<?xpacket end=\"w\"?>",
            _ => $"<x:xmpmeta xmlns:x=\"adobe:ns:meta/\">{rdf}</x:xmpmeta>"
        };
    }

    public static byte[] Iptc(Random random)
    {
        T Pick<T>(params T[] options) => options[random.Next(options.Length)];

        var block = new List<byte>();
        if (random.Next(3) == 0)
            block.AddRange(Pick(BlockFixture.DeclareUtf8, BlockFixture.DeclareLatin1));

        foreach (var number in new byte[] { 5, 105, 120, 25, 25, 25, 80, 80, 116 })
        {
            if (random.Next(2) == 0)
                continue;

            block.AddRange(random.Next(5) == 0
                ? BlockFixture.DataSet(2, number, Encoding.Latin1.GetBytes(Pick("Café", "Jörg", "© 2024")))
                : BlockFixture.Text(number, Pick("Title", "Unknown", "a;b", "Jörg", "Caption text", "Ann", "keyword", "Trailing\0")));
        }

        if (random.Next(8) == 0)
            block.AddRange([0, 0, .. BlockFixture.Text(5, "after a bad marker")]);

        return random.Next(12) == 0 ? "not iptc"u8.ToArray() : [.. block];
    }

    public static byte[] IccProfile(Random random) => random.Next(9) switch
    {
        0 => IccFixture.Profile("RGB ", 2, IccFixture.Desc("sRGB IEC61966-2.1")),
        1 => IccFixture.Profile("RGB ", 2, IccFixture.Desc("sRGB IEC61966-2.1", count: 5)),
        2 => IccFixture.Profile("RGB ", 4, IccFixture.Mluc(["enUS", "Display P3"])),
        3 => IccFixture.Profile("RGB ", 4, IccFixture.Mluc(["enUS", "Display P3"], ["deDE", "Anzeige P3"])),
        4 => IccFixture.Profile("GRAY", 2, IccFixture.Text("desc", "Gray text")),
        5 => IccFixture.Profile("CMYK", 2),
        6 => IccFixture.Profile("Lab ", 2, IccFixture.Desc("Generic Lab Profile")),
        7 => Enumerable.Range(0, 256).Select(i => (byte)(i * 7)).ToArray(),
        _ => IccFixture.Profile("RGB ", 2, IccFixture.Desc("  Padded  "))
    };
}