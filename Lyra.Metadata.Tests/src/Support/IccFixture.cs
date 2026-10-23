using System.Buffers.Binary;
using System.Text;

namespace Lyra.Metadata.Tests.Support;

internal static class IccFixture
{
    internal sealed record Tag(string Signature, byte[] Data);

    public static byte[] Profile(string colorSpace, byte major, params Tag[] tags)
    {
        var header = new byte[128];
        Encoding.Latin1.GetBytes(colorSpace).CopyTo(header, 16);
        "mntr"u8.CopyTo(header.AsSpan(12));
        "XYZ "u8.CopyTo(header.AsSpan(20));
        "acsp"u8.CopyTo(header.AsSpan(36));
        header[8] = major;

        var table = new List<byte>(BigEndian(tags.Length));
        var data = new List<byte>();
        var offset = 128 + 4 + 12 * tags.Length;

        foreach (var tag in tags)
        {
            table.AddRange(Encoding.ASCII.GetBytes(tag.Signature));
            table.AddRange(BigEndian(offset + data.Count));
            table.AddRange(BigEndian(tag.Data.Length));
            data.AddRange(tag.Data);
            while (data.Count % 4 != 0)
                data.Add(0);
        }

        byte[] profile = [.. header, .. table, .. data];
        BinaryPrimitives.WriteInt32BigEndian(profile, profile.Length);
        return profile;
    }

    /// <summary>A v2 textDescriptionType; <paramref name="count"/> defaults to the text plus its NUL.</summary>
    public static Tag Desc(string text, int? count = null) => new("desc", [.. "desc"u8, 0, 0, 0, 0, .. BigEndian(count ?? text.Length + 1), .. Encoding.ASCII.GetBytes(text), 0, .. new byte[79]]);

    public static Tag Text(string signature, string text) => new(signature, [.. "text"u8, 0, 0, 0, 0, .. Encoding.ASCII.GetBytes(text), 0]);

    /// <summary>A v4 multiLocalizedUnicodeType of (locale, text) records, such as ("enUS", "Display P3").</summary>
    public static Tag Mluc(params string[][] records)
    {
        var strings = records.Select(record => Encoding.BigEndianUnicode.GetBytes(record[1])).ToList();
        var bytes = new List<byte>([.. "mluc"u8, 0, 0, 0, 0, .. BigEndian(records.Length), .. BigEndian(12)]);

        var offset = 16 + 12 * records.Length;
        for (var i = 0; i < records.Length; i++)
        {
            bytes.AddRange(Encoding.ASCII.GetBytes(records[i][0]));
            bytes.AddRange(BigEndian(strings[i].Length));
            bytes.AddRange(BigEndian(offset));
            offset += strings[i].Length;
        }

        foreach (var text in strings)
            bytes.AddRange(text);

        return new Tag("desc", [.. bytes]);
    }

    private static byte[] BigEndian(int value)
    {
        var bytes = new byte[4];
        BinaryPrimitives.WriteInt32BigEndian(bytes, value);
        return bytes;
    }
}