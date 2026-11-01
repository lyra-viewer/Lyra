using System.Buffers.Binary;
using System.Globalization;
using System.Text;

namespace Lyra.ManagedCodecs.Scene.Fbx;

/// <summary>
/// Binary FBX: a tree of records, each giving the file offset where it ends, so whatever is not
/// part of the texture graph is skipped without being read. Array properties, where the mesh data
/// lives, are stepped over by their stored length and never inflated. Embedded media is located,
/// not copied.
/// </summary>
internal sealed class FbxBinaryReader
{
    private static ReadOnlySpan<byte> Magic => "Kaydara FBX Binary  \0"u8;

    private const int HeaderSize = 27;
    private const int MaxStringBytes = 64 * 1024;

    /// <summary>The texture graph reads no further than a connection's fourth property.</summary>
    private const int MaxProperties = 8;

    private readonly Stream _stream;
    private readonly long _length;
    private readonly bool _wide;
    private readonly CancellationToken _ct;
    private readonly IssueLog _issues = new();

    private readonly record struct Record(long Start, long End, long PropertyCount, long PropertiesStart, long PropertiesEnd, string Name);

    /// <summary>A property as far as the texture graph needs it: integers and strings by value, raw bytes by place.</summary>
    private readonly record struct Property(char Type, long Integer, string? Text, ByteRange Raw)
    {
        public string? Key => Type is 'L' or 'I' ? Integer.ToString(CultureInfo.InvariantCulture) : Text;
    }

    public static bool Recognizes(ReadOnlySpan<byte> head) => head.StartsWith(Magic);

    private FbxBinaryReader(Stream stream, uint version, CancellationToken ct)
    {
        _stream = stream;
        _length = stream.Length;
        _wide = version >= 7500;
        _ct = ct;
    }

    private int NullRecordSize => _wide ? 25 : 13;

    public static FbxDocument Read(Stream stream, CancellationToken ct)
    {
        Span<byte> header = stackalloc byte[HeaderSize];
        stream.Position = 0;
        if (stream.ReadAtLeast(header, HeaderSize, throwOnEndOfStream: false) < HeaderSize || !Recognizes(header))
            throw new InvalidDataException("Not a binary FBX file.");

        var version = BinaryPrimitives.ReadUInt32LittleEndian(header[23..]);
        return new FbxBinaryReader(stream, version, ct).ReadDocument();
    }

    private FbxDocument ReadDocument()
    {
        string? creator = null;
        var objects = new List<FbxObject>();
        var connections = new List<FbxConnection>();

        foreach (var record in Records(HeaderSize, _length))
        {
            _ct.ThrowIfCancellationRequested();

            switch (record.Name)
            {
                case "Creator":
                    creator ??= FirstString(record);
                    break;

                case "FBXHeaderExtension":
                    creator ??= Children(record).Where(child => child.Name == "Creator").Select(FirstString).FirstOrDefault();
                    break;

                case "Objects":
                    ReadObjects(record, objects);
                    break;

                case "Connections":
                    ReadConnections(record, connections);
                    break;
            }
        }

        return new FbxDocument(creator, objects, connections, _issues.ToList());
    }

    private void ReadObjects(Record objectsRecord, List<FbxObject> objects)
    {
        foreach (var record in Children(objectsRecord))
        {
            _ct.ThrowIfCancellationRequested();

            if (!FbxDocument.IsGraphType(record.Name))
                continue;

            var properties = Properties(record);
            if (properties.Count == 0 || properties[0].Key is not { } key)
            {
                Issue($"A {record.Name} at offset {record.Start} has no id.");
                continue;
            }

            // FBX 7: id, name, class. FBX 6: name, class, and the name is the id.
            var rawName = (properties[0].Type == 'S' ? properties[0].Text : properties.Skip(1).FirstOrDefault(property => property.Type == 'S').Text) ?? "";

            string? fileName = null, relative = null;
            FbxContent? content = null;

            foreach (var child in Children(record))
            {
                switch (child.Name)
                {
                    case "FileName" or "Filename":
                        fileName ??= FirstString(child);
                        break;

                    case "RelativeFilename":
                        relative ??= FirstString(child);
                        break;

                    case "Content":
                        if (Properties(child).FirstOrDefault(property => property.Type == 'R') is { Raw.Length: > 0 } raw)
                            content = new FbxContentRange(raw.Raw);
                        break;
                }
            }

            objects.Add(new FbxObject(key, record.Name, FbxDocument.DisplayName(rawName))
            {
                FileName = fileName,
                RelativeFilename = relative,
                Content = content
            });
        }
    }

    private void ReadConnections(Record connectionsRecord, List<FbxConnection> connections)
    {
        foreach (var record in Children(connectionsRecord))
        {
            _ct.ThrowIfCancellationRequested();

            if (record.Name is not ("C" or "Connect"))
                continue;

            var properties = Properties(record);
            if (properties.Count < 3 || properties[0].Text is not { } kind || properties[1].Key is not { } child || properties[2].Key is not { } parent)
                continue;

            connections.Add(new FbxConnection(kind, child, parent, properties.Count > 3 ? properties[3].Text : null));
        }
    }

    private IEnumerable<Record> Records(long start, long end)
    {
        for (var at = start; at + NullRecordSize <= end; )
        {
            if (ReadRecord(at, end) is not { } record)
                yield break;

            yield return record;
            at = record.End;
        }
    }

    private IEnumerable<Record> Children(Record parent) => Records(parent.PropertiesEnd, parent.End);

    private Record? ReadRecord(long at, long limit)
    {
        Span<byte> header = stackalloc byte[25];
        var size = _wide ? 25 : 13;

        if (!ReadAt(at, header[..size]))
        {
            Issue($"The file is cut short in a record header at offset {at}.");
            return null;
        }

        long end, count, propertyBytes;
        int nameLength;

        if (_wide)
        {
            end = (long)Math.Min(BinaryPrimitives.ReadUInt64LittleEndian(header), long.MaxValue);
            count = (long)Math.Min(BinaryPrimitives.ReadUInt64LittleEndian(header[8..]), long.MaxValue);
            propertyBytes = (long)Math.Min(BinaryPrimitives.ReadUInt64LittleEndian(header[16..]), long.MaxValue);
            nameLength = header[24];
        }
        else
        {
            end = BinaryPrimitives.ReadUInt32LittleEndian(header);
            count = BinaryPrimitives.ReadUInt32LittleEndian(header[4..]);
            propertyBytes = BinaryPrimitives.ReadUInt32LittleEndian(header[8..]);
            nameLength = header[12];
        }

        // The null record that closes a list of children.
        if (end == 0)
            return null;

        var propertiesStart = at + size + nameLength;
        if (end <= at || end > limit || propertiesStart > end || propertyBytes > end - propertiesStart)
        {
            Issue($"A record at offset {at} claims to end outside its parent.");
            return null;
        }

        Span<byte> name = stackalloc byte[nameLength];
        if (!ReadAt(at + size, name))
            return null;

        return new Record(at, end, count, propertiesStart, propertiesStart + propertyBytes, Encoding.ASCII.GetString(name));
    }

    private List<Property> Properties(Record record)
    {
        var properties = new List<Property>();
        var at = record.PropertiesStart;
        Span<byte> buffer = stackalloc byte[13];

        for (var i = 0L; i < Math.Min(record.PropertyCount, MaxProperties) && at < record.PropertiesEnd; i++)
        {
            if (!ReadAt(at, buffer[..1]))
                break;

            var type = (char)buffer[0];
            at++;

            long size;
            switch (type)
            {
                case 'Y':
                    size = 2;
                    break;

                case 'C':
                    size = 1;
                    break;

                case 'I' or 'F':
                    size = 4;
                    break;

                case 'L' or 'D':
                    size = 8;
                    break;

                case 'f' or 'd' or 'l' or 'i' or 'b':
                    if (!ReadAt(at, buffer[..12]))
                        return properties;

                    // Length, encoding, then the stored (perhaps deflated) byte count: stepped over, never inflated.
                    size = 12 + (long)BinaryPrimitives.ReadUInt32LittleEndian(buffer[8..]);
                    properties.Add(new Property(type, 0, null, default));
                    at += size;
                    continue;

                case 'S' or 'R':
                    if (!ReadAt(at, buffer[..4]))
                        return properties;

                    var length = (long)BinaryPrimitives.ReadUInt32LittleEndian(buffer);
                    var data = at + 4;

                    if (length > record.PropertiesEnd - data)
                    {
                        Issue($"A {record.Name} property at offset {at} runs past its record.");
                        return properties;
                    }

                    properties.Add(type == 'S'
                        ? new Property(type, 0, length <= MaxStringBytes ? ReadString(data, (int)length) : null, default)
                        : new Property(type, 0, null, new ByteRange(data, length)));

                    at = data + length;
                    continue;

                default:
                    Issue($"A {record.Name} has a property of unknown type '{type}' at offset {at - 1}.");
                    return properties;
            }

            if (size > record.PropertiesEnd - at || !ReadAt(at, buffer[..(int)size]))
                return properties;

            var integer = type switch
            {
                'Y' => BinaryPrimitives.ReadInt16LittleEndian(buffer),
                'C' => buffer[0],
                'I' => BinaryPrimitives.ReadInt32LittleEndian(buffer),
                'L' => BinaryPrimitives.ReadInt64LittleEndian(buffer),
                _ => 0L
            };

            properties.Add(new Property(type, integer, null, default));
            at += size;
        }

        return properties;
    }

    private string? FirstString(Record record) => Properties(record).FirstOrDefault(property => property.Type == 'S').Text;

    private string? ReadString(long at, int length)
    {
        var bytes = new byte[length];
        return ReadAt(at, bytes) ? Encoding.UTF8.GetString(bytes) : null;
    }

    private bool ReadAt(long at, Span<byte> into)
    {
        if (at < 0 || into.Length > _length - at)
            return false;

        _stream.Position = at;
        _stream.ReadExactly(into);
        return true;
    }

    private void Issue(string text) => _issues.Add(text);
}