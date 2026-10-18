using System.Buffers.Binary;
using System.Text;

namespace Lyra.Metadata.Tests.Support;

/// <summary>Builds IPTC-IIM datasets and Photoshop image resource blocks.</summary>
internal static class BlockFixture
{
    public static byte[] DataSet(byte record, byte number, byte[] value) =>
        [0x1C, record, number, (byte)(value.Length >> 8), (byte)value.Length, .. value];

    public static byte[] Text(byte number, string text) => DataSet(2, number, Encoding.UTF8.GetBytes(text));

    public static readonly byte[] DeclareUtf8 = DataSet(1, 90, [0x1B, 0x25, 0x47]);
    public static readonly byte[] DeclareLatin1 = DataSet(1, 90, [0x1B, 0x2D, 0x41]);

    public static byte[] Resource(ushort id, byte[] data, string name = "", string signature = "8BIM")
    {
        var nameBytes = Encoding.Latin1.GetBytes(name);
        var nameField = new List<byte> { (byte)nameBytes.Length };
        nameField.AddRange(nameBytes);
        if (nameField.Count % 2 != 0)
            nameField.Add(0);

        var size = new byte[4];
        BinaryPrimitives.WriteInt32BigEndian(size, data.Length);

        byte[] block = [.. Encoding.ASCII.GetBytes(signature), (byte)(id >> 8), (byte)id, .. nameField, .. size, .. data];
        return data.Length % 2 == 0 ? block : [.. block, 0];
    }
}
