using System.Buffers.Binary;
using Lyra.Imaging.Content;
using Lyra.Metadata.Containers;
using Lyra.Metadata.Iso;
using Lyra.Metadata.Tiff;

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

            List<Node>? nodes;
            if (head.AsSpan(6, 8).SequenceEqual("HEAPCCDR"u8))
                nodes = CiffParts.Describe(stream, head);
            else if (TiffDirectoryReader.IsTiff(head))
                nodes = TiffParts.Describe(stream, head);
            else if (IsIso(stream, head))
                nodes = IsoParts.Describe(stream);
            else if (RafContainer.Recognizes(head))
                nodes = RafParts.Describe(stream);
            else if (MrwContainer.Recognizes(head))
                nodes = MrwParts.Describe(stream);
            else
                nodes = null;

            return nodes is { Count: > 0 } ? Flatten(nodes) : null;
        }
        catch (Exception e) when (e is IOException or ArgumentException or InvalidOperationException)
        {
            return null;
        }
    }

    private static bool IsIso(Stream stream, byte[] head) => stream.Length >= 16 && IsoBoxes.MajorBrand(head) is not null;

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

    private static ushort U16(ReadOnlySpan<byte> b, bool little) => little ? BinaryPrimitives.ReadUInt16LittleEndian(b) : BinaryPrimitives.ReadUInt16BigEndian(b);

    private static uint U32(ReadOnlySpan<byte> b, bool little) => little ? BinaryPrimitives.ReadUInt32LittleEndian(b) : BinaryPrimitives.ReadUInt32BigEndian(b);

    private static byte[]? ReadAt(Stream stream, long offset, int count)
    {
        if (offset < 0 || count <= 0 || offset + count > stream.Length)
            return null;

        var bytes = new byte[count];
        stream.Position = offset;
        return stream.ReadAtLeast(bytes, count, throwOnEndOfStream: false) == count ? bytes : null;
    }

    /// <summary>The frame header of a JPEG at <paramref name="offset"/>, from as much of it as precedes the image data.</summary>
    private static JpegFacts? JpegFrame(Stream stream, long offset, long size) =>
        ReadAt(stream, offset, (int)Math.Min(size, 256 * 1024)) is { } bytes ? JpegContainer.Read(bytes)?.Fact<JpegFacts>() : null;

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
