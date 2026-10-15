using System.Buffers.Binary;
using Lyra.Imaging.Content;

namespace Lyra.Imaging.Decoding.Structure;

/// <summary>
/// The layout of a camera raw for the structure inspector: the containers it is built from, where
/// each part starts and how big it is. Directories nest under whatever points at them; data blocks
/// (embedded JPEGs, the raw data itself) stand at the top in file order and name their owner.
/// Reads structure only, never pixels, and trusts no offset in the file.
/// </summary>
internal static partial class RawStructure
{
    private sealed class Node(string name, string? description, long offset, long size)
    {
        public string Name { get; } = name;
        public string? Description { get; } = description;
        public long Offset { get; } = offset;
        public long Size { get; } = size;
        public List<KeyValuePair<string, string>> Fields { get; } = [];
        public List<Node> Children { get; } = [];
    }

    public static IReadOnlyList<StructureGroup>? Describe(Stream stream)
    {
        try
        {
            if (!stream.CanSeek || ReadAt(stream, 0, 16) is not { } head)
                return null;

            var nodes =
                head.AsSpan(6, 8).SequenceEqual("HEAPCCDR"u8) ? CiffParts.Describe(stream, head) :
                IsTiff(head) ? TiffParts.Describe(stream, head) :
                IsIso(stream, head) ? IsoParts.Describe(stream) :
                head.AsSpan().StartsWith("FUJIFILMCCD-RAW"u8) ? RafParts.Describe(stream) :
                head.AsSpan(0, 4).SequenceEqual("\0MRM"u8) ? MrwParts.Describe(stream) :
                null;

            return nodes is { Count: > 0 } ? Flatten(nodes) : null;
        }
        catch (Exception e) when (e is IOException or ArgumentException or InvalidOperationException)
        {
            return null;
        }
    }

    private static bool IsTiff(byte[] head) => (head[0] == 'I' && head[1] == 'I') || (head[0] == 'M' && head[1] == 'M');

    private static bool IsIso(Stream stream, byte[] head) => stream.Length >= 16 && head.AsSpan(4, 4).SequenceEqual("ftyp"u8);

    /// <summary>Top-level parts in file order, each followed by its own parts in file order.</summary>
    private static List<StructureGroup> Flatten(List<Node> nodes)
    {
        var groups = new List<StructureGroup>();

        void Add(Node node, int depth)
        {
            groups.Add(new StructureGroup
            {
                Name = node.Name,
                Description = node.Description,
                Offset = node.Offset,
                SizeBytes = node.Size,
                Depth = depth,
                Fields = node.Fields
            });

            foreach (var child in node.Children.OrderBy(c => c.Offset))
                Add(child, depth + 1);
        }

        foreach (var node in nodes.OrderBy(n => n.Offset))
            Add(node, 0);

        return groups;
    }

    // ------------------------------------------------------------------------------------------
    //  Shared helpers
    // ------------------------------------------------------------------------------------------

    private static byte[]? ReadAt(Stream stream, long offset, int count)
    {
        if (offset < 0 || count <= 0 || offset + count > stream.Length)
            return null;

        var bytes = new byte[count];
        stream.Position = offset;
        return stream.ReadAtLeast(bytes, count, throwOnEndOfStream: false) == count ? bytes : null;
    }

    private readonly record struct JpegFrameInfo(int Width, int Height, bool Lossless);

    /// <summary>The frame size of a JPEG at <paramref name="offset"/>, and whether it is lossless (as raw data is).</summary>
    private static JpegFrameInfo? JpegFrame(Stream stream, long offset, long size)
    {
        var bytes = ReadAt(stream, offset, (int)Math.Min(size, 256 * 1024));
        if (bytes is null || bytes.Length < 4 || bytes[0] != 0xFF || bytes[1] != 0xD8)
            return null;

        var i = 2;
        while (i + 4 <= bytes.Length)
        {
            if (bytes[i] != 0xFF)
                return null;

            var marker = bytes[i + 1];
            if (marker == 0xFF)
            {
                i++;
                continue;
            }

            if (marker is 0xD9 or 0xDA)
                return null;

            var length = BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(i + 2));
            if (marker is >= 0xC0 and <= 0xCF and not (0xC4 or 0xC8 or 0xCC) && i + 9 <= bytes.Length)
                return new JpegFrameInfo(BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(i + 7)), BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(i + 5)), marker == 0xC3);

            i += 2 + length;
        }

        return null;
    }

    private static Node JpegNode(Stream stream, long offset, long size, string owner)
    {
        var frame = JpegFrame(stream, offset, size);
        var name = frame is { } f ? $"Embedded JPEG {f.Width} x {f.Height}" : "Embedded JPEG";
        var node = new Node(name, owner, offset, size);

        if (frame is { } dims)
        {
            node.Fields.Add(new("Width", $"{dims.Width} px"));
            node.Fields.Add(new("Height", $"{dims.Height} px"));
        }

        return node;
    }

    private static string Hex(long value) => $"0x{value:X}";
}
