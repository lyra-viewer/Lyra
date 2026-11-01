using System.Buffers.Binary;
using System.Text;

namespace Lyra.ManagedCodecs.Tests.Scene;

internal sealed class GltfFixture : IDisposable
{
    public string Folder { get; } = Directory.CreateTempSubdirectory("lyra-scene-").FullName;

    public string Write(string relativePath, byte[] bytes)
    {
        var path = Path.Combine(Folder, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, bytes);
        return path;
    }

    public string Write(string relativePath, string text) => Write(relativePath, Encoding.UTF8.GetBytes(text));

    public void Dispose()
    {
        try
        {
            Directory.Delete(Folder, recursive: true);
        }
        catch (IOException) { }
    }

    /// <summary>Stand-in image bytes. Never decoded, only located.</summary>
    public static byte[] Payload(byte tag, int length = 16) => [.. Enumerable.Range(0, length).Select(i => (byte)(tag + i))];

    /// <summary>A GLB with chunks padded the way the spec asks: JSON with spaces, BIN with zeros.</summary>
    public static byte[] Glb(string json, byte[]? binary = null, uint version = 2)
    {
        var jsonBytes = Pad(Encoding.UTF8.GetBytes(json), (byte)' ');
        var binBytes = binary is null ? null : Pad(binary, 0);

        var length = 12 + 8 + jsonBytes.Length + (binBytes is null ? 0 : 8 + binBytes.Length);
        var glb = new byte[length];
        var span = glb.AsSpan();

        BinaryPrimitives.WriteUInt32LittleEndian(span, 0x46546C67);
        BinaryPrimitives.WriteUInt32LittleEndian(span[4..], version);
        BinaryPrimitives.WriteUInt32LittleEndian(span[8..], (uint)length);

        var at = 12;
        at = Chunk(span, at, 0x4E4F534A, jsonBytes);
        if (binBytes is not null)
            Chunk(span, at, 0x004E4942, binBytes);

        return glb;
    }

    /// <summary>Where the BIN chunk's data starts in a file made by <see cref="Glb"/>.</summary>
    public static long BinaryOffset(string json) => 12 + 8 + Pad(Encoding.UTF8.GetBytes(json), (byte)' ').Length + 8;

    private static int Chunk(Span<byte> span, int at, uint type, byte[] data)
    {
        BinaryPrimitives.WriteUInt32LittleEndian(span[at..], (uint)data.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(span[(at + 4)..], type);
        data.CopyTo(span[(at + 8)..]);
        return at + 8 + data.Length;
    }

    private static byte[] Pad(byte[] data, byte filler)
    {
        var padded = new byte[(data.Length + 3) & ~3];
        Array.Fill(padded, filler);
        data.CopyTo(padded, 0);
        return padded;
    }
}