using System.Text;
using Lyra.Metadata.Xmp;
using Xunit;

namespace Lyra.Metadata.Tests.Xmp;

public class XmpPacketTests
{
    private const string Rdf = "xmlns:rdf=\"http://www.w3.org/1999/02/22-rdf-syntax-ns#\"";
    private const string Dc = "xmlns:dc=\"http://purl.org/dc/elements/1.1/\"";
    private const string XmpNs = "xmlns:xmp=\"http://ns.adobe.com/xap/1.0/\"";

    private static XmpPacket Parse(string description, string wrapperStart = "<x:xmpmeta xmlns:x=\"adobe:ns:meta/\">", string wrapperEnd = "</x:xmpmeta>") =>
        XmpPacket.Parse(Encoding.UTF8.GetBytes($"{wrapperStart}<rdf:RDF {Rdf}>{description}</rdf:RDF>{wrapperEnd}"))!;

    [Fact]
    public void ElementFormArraysAndLanguageAlternativesAreRead()
    {
        var packet = Parse($"""
                            <rdf:Description rdf:about="" {Dc} {XmpNs}>
                              <dc:title><rdf:Alt><rdf:li xml:lang="x-default">Sunset</rdf:li><rdf:li xml:lang="de">Abend</rdf:li></rdf:Alt></dc:title>
                              <dc:subject><rdf:Bag><rdf:li>leaf</rdf:li><rdf:li>nature</rdf:li></rdf:Bag></dc:subject>
                              <dc:creator><rdf:Seq><rdf:li>Jane</rdf:li></rdf:Seq></dc:creator>
                              <xmp:Rating>3</xmp:Rating>
                            </rdf:Description>
                            """);

        Assert.Equal("Sunset", packet.First(XmpNamespaces.Dc, "title"));
        Assert.Equal(["leaf", "nature"], packet.Values(XmpNamespaces.Dc, "subject"));
        Assert.Equal("Jane", packet.First(XmpNamespaces.Dc, "creator"));
        Assert.Equal("3", packet.First(XmpNamespaces.Xmp, "Rating"));
    }

    [Fact]
    public void AttributeFormPropertiesAreRead()
    {
        var packet = Parse($"""<rdf:Description rdf:about="" {XmpNs} xmp:Rating="5" xmp:CreatorTool="Lyra"/>""");

        Assert.Equal("5", packet.First(XmpNamespaces.Xmp, "Rating"));
        Assert.Equal("Lyra", packet.First(XmpNamespaces.Xmp, "CreatorTool"));
    }

    [Fact]
    public void PropertiesAreFoundByNamespaceWhateverThePrefix()
    {
        var packet = Parse("""<rdf:Description rdf:about="" xmlns:d="http://purl.org/dc/elements/1.1/" xmlns:a="http://ns.adobe.com/xap/1.0/" a:Rating="4"><d:rights>(c) me</d:rights></rdf:Description>""");

        Assert.Equal("4", packet.First(XmpNamespaces.Xmp, "Rating"));
        Assert.Equal("(c) me", packet.First(XmpNamespaces.Dc, "rights"));
    }

    [Fact]
    public void TheDefaultLanguageComesFirstWhereverItIsWritten()
    {
        var packet = Parse($"""<rdf:Description {Dc}><dc:title><rdf:Alt><rdf:li xml:lang="de">Abend</rdf:li><rdf:li xml:lang="x-default">Sunset</rdf:li></rdf:Alt></dc:title></rdf:Description>""");

        Assert.Equal(["Sunset", "Abend"], packet.Values(XmpNamespaces.Dc, "title"));
    }

    [Fact]
    public void LocalNamesIgnoreCase()
    {
        var packet = Parse($"""<rdf:Description {Dc}><dc:Format>image/tiff</dc:Format></rdf:Description>""");

        Assert.Equal("image/tiff", packet.First(XmpNamespaces.Dc, "format"));
    }

    [Fact]
    public void PropertiesOfSeveralDescriptionsAreAllRead()
    {
        var packet = Parse($"""<rdf:Description {Dc} dc:format="image/jpeg"/><rdf:Description {XmpNs} xmp:Rating="2"/>""");

        Assert.Equal("image/jpeg", packet.First(XmpNamespaces.Dc, "format"));
        Assert.Equal("2", packet.First(XmpNamespaces.Xmp, "Rating"));
    }

    [Fact]
    public void StructsAreKeptButHaveNoValue()
    {
        var packet = Parse($"""
                            <rdf:Description {XmpNs} xmlns:st="http://ns.adobe.com/xap/1.0/sType/Dimensions#">
                              <xmp:Thumbnails rdf:parseType="Resource"><st:w>256</st:w></xmp:Thumbnails>
                              <xmp:Nested><rdf:Description st:h="128"/></xmp:Nested>
                              <xmp:History><rdf:Seq><rdf:li rdf:parseType="Resource"><st:w>1</st:w></rdf:li></rdf:Seq></xmp:History>
                            </rdf:Description>
                            """);

        Assert.Empty(packet.Values(XmpNamespaces.Xmp, "Thumbnails"));
        Assert.Empty(packet.Values(XmpNamespaces.Xmp, "History"));

        var thumbnails = packet.Properties.Single(p => p.Name == "Thumbnails");
        Assert.Equal("256", thumbnails.Items[0].Fields?.Single().Items[0].Value);

        var nested = packet.Properties.Single(p => p.Name == "Nested");
        Assert.Equal("128", nested.Items[0].Fields?.Single().Items[0].Value);
    }

    [Fact]
    public void AResourceReferenceIsItsValue()
    {
        var packet = Parse("""<rdf:Description xmlns:xmpRights="http://ns.adobe.com/xap/1.0/rights/"><xmpRights:WebStatement rdf:resource="https://example.com/rights"/></rdf:Description>""");

        Assert.Equal("https://example.com/rights", packet.First(XmpNamespaces.XmpRights, "WebStatement"));
    }

    [Fact]
    public void ABareRdfRootAndThePacketWrapperBothWork()
    {
        var bare = XmpPacket.Parse(Encoding.UTF8.GetBytes($"""<rdf:RDF {Rdf}><rdf:Description {XmpNs} xmp:Rating="1"/></rdf:RDF>"""));
        var wrapped = XmpPacket.Parse(Encoding.UTF8.GetBytes($"""
                                                              <?xpacket begin="﻿" id="W5M0MpCehiHzreSzNTczkc9d"?>
                                                              <x:xmpmeta xmlns:x="adobe:ns:meta/"><rdf:RDF {Rdf}><rdf:Description {XmpNs} xmp:Rating="1"/></rdf:RDF></x:xmpmeta>
                                                              {new string(' ', 2000)}
                                                              <?xpacket end="w"?>
                                                              """));

        Assert.Equal("1", bare?.First(XmpNamespaces.Xmp, "Rating"));
        Assert.Equal("1", wrapped?.First(XmpNamespaces.Xmp, "Rating"));
    }

    [Fact]
    public void WhatFollowsTheRootIsIgnored()
    {
        var bytes = Encoding.UTF8.GetBytes($"""<rdf:RDF {Rdf}><rdf:Description {XmpNs} xmp:Rating="1"/></rdf:RDF>""").Concat(new byte[64]).Concat("garbage<<"u8.ToArray()).ToArray();

        Assert.Equal("1", XmpPacket.Parse(bytes)?.First(XmpNamespaces.Xmp, "Rating"));
    }

    [Fact]
    public void Utf16PacketsAreRead()
    {
        var bytes = new UnicodeEncoding(bigEndian: false, byteOrderMark: true).GetPreamble()
            .Concat(Encoding.Unicode.GetBytes($"""<rdf:RDF {Rdf}><rdf:Description {Dc}><dc:creator><rdf:Seq><rdf:li>Jörg</rdf:li></rdf:Seq></dc:creator></rdf:Description></rdf:RDF>""")).ToArray();

        Assert.Equal("Jörg", XmpPacket.Parse(bytes)?.First(XmpNamespaces.Dc, "creator"));
    }

    [Theory]
    [InlineData("<x:xmpmeta><rdf:RDF")]
    [InlineData("not xml at all")]
    [InlineData("<?xml version=\"1.0\"?><!DOCTYPE lolz [<!ENTITY lol \"lol\"><!ENTITY lol2 \"&lol;&lol;&lol;&lol;\">]><rdf:RDF xmlns:rdf=\"http://www.w3.org/1999/02/22-rdf-syntax-ns#\">&lol2;</rdf:RDF>")]
    [InlineData("<!DOCTYPE x SYSTEM \"file:///etc/passwd\"><x>&xxe;</x>")]
    [InlineData("")]
    public void WhatIsNotSafeWellFormedXmlIsNoPacket(string text)
    {
        Assert.Null(XmpPacket.Parse(Encoding.UTF8.GetBytes(text)));
    }

    [Fact]
    public void ARootWithoutRdfHasNoProperties()
    {
        Assert.Empty(XmpPacket.Parse("<x:xmpmeta xmlns:x=\"adobe:ns:meta/\"/>"u8)!.Properties);
    }

    [Fact]
    public void DamagedPacketsNeverThrow()
    {
        var packet = Encoding.UTF8.GetBytes($"""
                                             <x:xmpmeta xmlns:x="adobe:ns:meta/"><rdf:RDF {Rdf}><rdf:Description {Dc} {XmpNs} xmp:Rating="3">
                                             <dc:title><rdf:Alt><rdf:li xml:lang="x-default">Sunset</rdf:li></rdf:Alt></dc:title>
                                             <dc:subject><rdf:Bag><rdf:li>leaf</rdf:li><rdf:li>nature</rdf:li></rdf:Bag></dc:subject>
                                             </rdf:Description></rdf:RDF></x:xmpmeta>
                                             """);

        var random = new Random(77);
        for (var variant = 0; variant < 200; variant++)
        {
            var damaged = variant % 3 == 0 ? packet[..random.Next(packet.Length)] : (byte[])packet.Clone();
            if (variant % 3 != 0)
                for (var i = 0; i < 1 + variant % 6; i++)
                    damaged[random.Next(damaged.Length)] ^= (byte)(1 << random.Next(8));

            _ = XmpPacket.Parse(damaged)?.Values(XmpNamespaces.Dc, "subject");
        }
    }

    [Fact]
    public void AnAliasStandsInForAnAbsentProperty()
    {
        var packet = Parse("""<rdf:Description xmlns:pdf="http://ns.adobe.com/pdf/1.3/" xmlns:photoshop="http://ns.adobe.com/photoshop/1.0/" pdf:Author="Pa"><photoshop:Caption>Cap</photoshop:Caption></rdf:Description>""");

        Assert.Equal("Pa", packet.First(XmpNamespaces.Dc, "creator"));
        Assert.Equal("Cap", packet.First(XmpNamespaces.Dc, "description"));
    }

    [Fact]
    public void ThePropertyItselfWinsOverItsAliasesAndTheFirstAliasOverTheRest()
    {
        var withBase = Parse($"""<rdf:Description {Dc} xmlns:pdf="http://ns.adobe.com/pdf/1.3/"><pdf:Author>Pa</pdf:Author><dc:creator><rdf:Seq><rdf:li>Dc</rdf:li></rdf:Seq></dc:creator></rdf:Description>""");
        var twoAliases = Parse("""<rdf:Description xmlns:pdf="http://ns.adobe.com/pdf/1.3/" xmlns:tiff="http://ns.adobe.com/tiff/1.0/"><tiff:Artist>Ta</tiff:Artist><pdf:Author>Pa</pdf:Author></rdf:Description>""");

        Assert.Equal("Dc", withBase.First(XmpNamespaces.Dc, "creator"));
        Assert.Equal("Ta", twoAliases.First(XmpNamespaces.Dc, "creator"));
    }

    [Fact]
    public void Windows1252BytesInAUtf8PacketAreReadAsThoseCharacters()
    {
        byte[] packet = [.. Encoding.UTF8.GetBytes($"""<rdf:RDF {Rdf}><rdf:Description {Dc}><dc:rights>"""), 0x93, (byte)'C', (byte)'a', (byte)'f', 0xE9, 0x94, .. Encoding.UTF8.GetBytes(" Jörg</dc:rights></rdf:Description></rdf:RDF>")];

        Assert.Equal("\u201CCafé\u201D Jörg", XmpPacket.Parse(packet)?.First(XmpNamespaces.Dc, "rights"));
    }

    [Fact]
    public void EveryWindows1252ByteHasACharacter()
    {
        for (var b = 0x80; b <= 0x9F; b++)
        {
            byte[] packet = [.. Encoding.UTF8.GetBytes($"""<rdf:RDF {Rdf}><rdf:Description {Dc}><dc:rights>"""), (byte)b, .. Encoding.UTF8.GetBytes("</dc:rights></rdf:Description></rdf:RDF>")];
            var value = XmpPacket.Parse(packet)?.First(XmpNamespaces.Dc, "rights");

            Assert.True(value is { Length: 1 }, $"0x{b:X2} gave '{value}'");
        }
    }
}