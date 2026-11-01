using System.Buffers.Binary;

namespace Lyra.ManagedCodecs.Scene.Gltf;

/// <summary>
/// The binary glTF wrapper: a 12-byte header, then chunks. Reads the JSON chunk and only notes
/// where the binary chunk is, so a large GLB costs no more to open than its JSON.
/// </summary>
internal sealed record GlbContainer(byte[] Json, ByteRange? Binary, IReadOnlyList<string> Issues)
{
    public const uint Magic = 0x46546C67; // "glTF"

    private const uint JsonChunk = 0x4E4F534A;   // "JSON"
    private const uint BinaryChunk = 0x004E4942; // "BIN\0"
    private const int HeaderSize = 12;
    private const int ChunkHeaderSize = 8;

    public static bool Recognizes(ReadOnlySpan<byte> head) => head.Length >= 4 && BinaryPrimitives.ReadUInt32LittleEndian(head) == Magic;

    public static GlbContainer Read(Stream stream, long maxJsonBytes)
    {
        Span<byte> header = stackalloc byte[HeaderSize];
        if (stream.ReadAtLeast(header, HeaderSize, throwOnEndOfStream: false) < HeaderSize || !Recognizes(header))
            throw new InvalidDataException("Not a binary glTF file.");

        var version = BinaryPrimitives.ReadUInt32LittleEndian(header[4..]);
        if (version != 2)
            throw new NotSupportedException($"Binary glTF version {version} is not supported, only version 2.");

        var issues = new List<string>();
        var declared = (long)BinaryPrimitives.ReadUInt32LittleEndian(header[8..]);
        var end = stream.Length;

        if (declared > end)
            issues.Add($"The file is truncated: its header gives {declared} bytes, but it holds {end}.");
        else
            end = declared;

        byte[]? json = null;
        ByteRange? binary = null;
        Span<byte> chunk = stackalloc byte[ChunkHeaderSize];

        for (var at = (long)HeaderSize; at + ChunkHeaderSize <= end; )
        {
            stream.Position = at;
            stream.ReadExactly(chunk);

            var length = (long)BinaryPrimitives.ReadUInt32LittleEndian(chunk);
            var type = BinaryPrimitives.ReadUInt32LittleEndian(chunk[4..]);
            var data = at + ChunkHeaderSize;
            var available = Math.Min(length, end - data);

            if (available < length)
                issues.Add($"Chunk 0x{type:X8} is cut short: {available} of {length} bytes.");

            if (type == JsonChunk && json is null)
            {
                if (available > maxJsonBytes)
                    throw new InvalidDataException($"The glTF JSON is {available} bytes, more than the {maxJsonBytes} that are read.");

                json = new byte[available];
                stream.ReadExactly(json);
            }
            else if (type == BinaryChunk && binary is null)
            {
                binary = new ByteRange(data, available);
            }

            at = data + length;
        }

        if (json is null)
            throw new InvalidDataException("The binary glTF file has no JSON chunk.");

        return new GlbContainer(json, binary, issues);
    }
}