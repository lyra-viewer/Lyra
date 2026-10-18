using System.Buffers.Binary;
using System.Text;
using Lyra.Metadata.Support;

namespace Lyra.Metadata.Iptc;

public sealed record IptcEntry(byte Record, byte DataSet, string Text);

/// <summary>
/// An IPTC-IIM block: datasets in file order. Text follows the Coded Character Set (1:90) once it
/// has been declared; before that, or for a set it does not name, each value is UTF-8 when it is
/// valid UTF-8 and Latin-1 otherwise. Reading stops at the first thing that is not a dataset and
/// keeps what came before it.
/// </summary>
public sealed class IptcRecords : IMetadataBlock<IptcRecords>
{
    public const byte EnvelopeRecord = 1;
    public const byte ApplicationRecord = 2;

    public const byte CodedCharacterSet = 90;
    public const byte ObjectName = 5;
    public const byte Keywords = 25;
    public const byte ByLine = 80;
    public const byte Headline = 105;
    public const byte CopyrightNotice = 116;
    public const byte Caption = 120;

    private const byte TagMarker = 0x1C;
    private const int MaxEntries = 10_000;


    public IReadOnlyList<IptcEntry> Entries { get; }

    private IptcRecords(IReadOnlyList<IptcEntry> entries) => Entries = entries;

    /// <summary>The datasets, or null when the block does not start with one.</summary>
    public static IptcRecords? Parse(ReadOnlySpan<byte> data)
    {
        if (data.IsEmpty || data[0] != TagMarker)
            return null;

        var entries = new List<IptcEntry>();
        Encoding? declared = null;
        var offset = 0;

        while (offset + 5 <= data.Length && data[offset] == TagMarker && entries.Count < MaxEntries)
        {
            var record = data[offset + 1];
            var dataSet = data[offset + 2];
            int length = BinaryPrimitives.ReadUInt16BigEndian(data[(offset + 3)..]);
            offset += 5;

            // The extended form: the low 15 bits say how many bytes the real length takes.
            if ((length & 0x8000) != 0)
            {
                var size = length & 0x7FFF;
                if (size is 0 or > 4 || offset + size > data.Length)
                    break;

                long extended = 0;
                for (var i = 0; i < size; i++)
                    extended = (extended << 8) | data[offset + i];

                offset += size;
                if (extended > Limits.BlockBytes)
                    break;

                length = (int)extended;
            }

            if (length > data.Length - offset)
                break;

            var value = data.Slice(offset, length);
            offset += length;

            if (record == EnvelopeRecord && dataSet == CodedCharacterSet)
                declared = EncodingOf(value);

            entries.Add(new IptcEntry(record, dataSet, Decode(value, declared)));
        }

        return new IptcRecords(entries);
    }

    /// <summary>The text of every occurrence of a dataset, in file order.</summary>
    public IReadOnlyList<string> Get(byte record, byte dataSet) => Entries.Where(entry => entry.Record == record && entry.DataSet == dataSet).Select(entry => entry.Text).ToList();

    /// <summary>The ISO 2022 escape sequences that name UTF-8 and Latin-1; null for anything else.</summary>
    private static Encoding? EncodingOf(ReadOnlySpan<byte> sequence) => sequence switch
    {
        [0x1B, 0x25, 0x47, ..] => Encoding.UTF8,
        [0x1B, 0x2D, 0x41, ..] => Encoding.Latin1,
        _ => null
    };

    private static string Decode(ReadOnlySpan<byte> value, Encoding? declared) => declared?.GetString(value) ?? ByteText.Utf8OrLatin1(value);
}