using System.Text;
using Lyra.Metadata.Icc;
using Lyra.Metadata.Iptc;
using Lyra.Metadata.Tests.Support;
using Lyra.Metadata.Xmp;
using Xunit;
using static Lyra.Metadata.Tests.Support.Golden;

namespace Lyra.Metadata.Tests;

public class BlockGoldenTests
{
    private readonly record struct XmpField(string Namespace, string Name, bool AllValues = false);

    private static readonly XmpField[] XmpFields =
    [
        new(XmpNamespaces.Dc, "title"), new(XmpNamespaces.Dc, "description"), new(XmpNamespaces.Dc, "subject", AllValues: true),
        new(XmpNamespaces.Dc, "creator"), new(XmpNamespaces.Dc, "rights"), new(XmpNamespaces.Xmp, "Rating")
    ];

    [Fact]
    public void XmpReadsAsRecorded()
    {
        var random = new Random(20261018);
        var lines = new List<string>();

        for (var sample = 0; sample < 1000; sample++)
        {
            var packet = XmpPacket.Parse(Encoding.UTF8.GetBytes(RandomBlocks.Xmp(random)));
            var cells = XmpFields.Select(field =>
            {
                var values = packet?.Values(field.Namespace, field.Name).Where(v => !string.IsNullOrWhiteSpace(v)).Select(v => v.Trim()).ToList() ?? [];
                return Cell(string.Join(" | ", field.AllValues ? values : values.Take(1)));
            });

            lines.Add(string.Join('\t', [sample.ToString(), packet is null ? "unread" : "read", .. cells]));
        }

        Verify("xmp", lines);
    }

    [Fact]
    public void IptcReadsAsRecorded()
    {
        var random = new Random(20261019);
        byte[] dataSets = [IptcRecords.ObjectName, IptcRecords.Headline, IptcRecords.Caption, IptcRecords.Keywords, IptcRecords.ByLine, IptcRecords.CopyrightNotice];
        var lines = new List<string>();

        for (var sample = 0; sample < 300; sample++)
        {
            var records = IptcRecords.Parse(RandomBlocks.Iptc(random));
            var cells = dataSets.Select(dataSet => Cell(string.Join(dataSet == IptcRecords.Keywords ? ';' : ' ', records?.Get(IptcRecords.ApplicationRecord, dataSet) ?? [])));

            lines.Add(string.Join('\t', [sample.ToString(), records is null ? "unread" : "read", .. cells]));
        }

        Verify("iptc", lines);
    }

    [Fact]
    public void IccReadsAsRecorded()
    {
        var random = new Random(20261020);
        var lines = new List<string>();

        for (var sample = 0; sample < 200; sample++)
        {
            var profile = IccProfile.Parse(RandomBlocks.IccProfile(random));
            lines.Add(string.Join('\t', sample.ToString(), Cell(profile?.ColorSpace), Cell(profile?.Description)));
        }

        Verify("icc", lines);
    }
}