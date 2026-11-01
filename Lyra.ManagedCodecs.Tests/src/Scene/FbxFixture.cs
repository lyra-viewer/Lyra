using System.Buffers.Binary;
using System.Text;

namespace Lyra.ManagedCodecs.Tests.Scene;

/// <summary>An array property as stored: element type, count, encoding and the stored bytes, which are never inflated.</summary>
internal sealed record FbxArray(char Type, uint Count, uint Encoding, byte[] Stored);

/// <summary>A binary FBX record: a name, typed properties and children.</summary>
internal sealed class FbxNode(string name, params object[] properties)
{
    public string Name { get; } = name;
    public object[] Properties { get; } = properties;
    public List<FbxNode> Children { get; } = [];

    public FbxNode With(params FbxNode[] children)
    {
        Children.AddRange(children);
        return this;
    }
}

/// <summary>Writes binary FBX: 13-byte record headers before version 7500, 25-byte ones from it.</summary>
internal static class FbxFixture
{
    /// <summary>The name binary FBX 7 gives an object: its own name, then its class.</summary>
    public static string Named(string name, string type) => $"{name}\0\u0001{type}";

    public static byte[] Binary(uint version, params FbxNode[] topLevel)
    {
        var wide = version >= 7500;
        using var stream = new MemoryStream();

        stream.Write("Kaydara FBX Binary  \0"u8);
        stream.Write([0x1A, 0x00]);
        WriteUInt32(stream, version);

        foreach (var node in topLevel)
            WriteNode(stream, node, wide);

        stream.Write(new byte[wide ? 25 : 13]);
        return stream.ToArray();
    }

    /// <summary>Where <paramref name="payload"/> first appears in <paramref name="file"/>.</summary>
    public static long OffsetOf(byte[] file, byte[] payload) => file.AsSpan().IndexOf(payload);

    private static void WriteNode(MemoryStream stream, FbxNode node, bool wide)
    {
        var start = stream.Position;
        var headerSize = wide ? 25 : 13;

        stream.Write(new byte[headerSize]);
        stream.Write(Encoding.ASCII.GetBytes(node.Name));

        var propertiesStart = stream.Position;
        foreach (var property in node.Properties)
            WriteProperty(stream, property);

        var propertyBytes = stream.Position - propertiesStart;

        if (node.Children.Count > 0)
        {
            foreach (var child in node.Children)
                WriteNode(stream, child, wide);

            stream.Write(new byte[headerSize]);
        }

        var end = stream.Position;
        var header = new byte[headerSize];

        if (wide)
        {
            BinaryPrimitives.WriteUInt64LittleEndian(header, (ulong)end);
            BinaryPrimitives.WriteUInt64LittleEndian(header.AsSpan(8), (ulong)node.Properties.Length);
            BinaryPrimitives.WriteUInt64LittleEndian(header.AsSpan(16), (ulong)propertyBytes);
            header[24] = (byte)node.Name.Length;
        }
        else
        {
            BinaryPrimitives.WriteUInt32LittleEndian(header, (uint)end);
            BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(4), (uint)node.Properties.Length);
            BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(8), (uint)propertyBytes);
            header[12] = (byte)node.Name.Length;
        }

        stream.Position = start;
        stream.Write(header);
        stream.Position = end;
    }

    private static void WriteProperty(MemoryStream stream, object property)
    {
        switch (property)
        {
            case long value:
                stream.WriteByte((byte)'L');
                Span<byte> int64 = stackalloc byte[8];
                BinaryPrimitives.WriteInt64LittleEndian(int64, value);
                stream.Write(int64);
                break;

            case int value:
                stream.WriteByte((byte)'I');
                WriteUInt32(stream, (uint)value);
                break;

            case double value:
                stream.WriteByte((byte)'D');
                Span<byte> float64 = stackalloc byte[8];
                BinaryPrimitives.WriteDoubleLittleEndian(float64, value);
                stream.Write(float64);
                break;

            case string value:
                var text = Encoding.UTF8.GetBytes(value);
                stream.WriteByte((byte)'S');
                WriteUInt32(stream, (uint)text.Length);
                stream.Write(text);
                break;

            case byte[] value:
                stream.WriteByte((byte)'R');
                WriteUInt32(stream, (uint)value.Length);
                stream.Write(value);
                break;

            case FbxArray array:
                stream.WriteByte((byte)array.Type);
                WriteUInt32(stream, array.Count);
                WriteUInt32(stream, array.Encoding);
                WriteUInt32(stream, (uint)array.Stored.Length);
                stream.Write(array.Stored);
                break;

            default:
                throw new ArgumentException($"No FBX property type for {property.GetType()}.");
        }
    }

    private static void WriteUInt32(MemoryStream stream, uint value)
    {
        Span<byte> bytes = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, value);
        stream.Write(bytes);
    }
}