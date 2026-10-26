using System.Buffers.Binary;
using Lyra.Metadata.Containers;

namespace Lyra.Imaging.Decoding.Structure;

internal static partial class RawStructure
{
    // Fujifilm RAF

    private static class RafParts
    {
        public static List<Node> Describe(Stream stream)
        {
            if (ReadAt(stream, 0, RafContainer.HeaderLength) is not { } head || RafContainer.ReadHeader(head) is not { } raf)
                return [];

            var header = new Node("RAF header", $"Fujifilm {raf.Camera}".Trim(), 0, RafContainer.HeaderLength);
            header.Fields.Add(new("Format version", raf.FormatVersion));
            header.Fields.Add(new("Camera", raf.Camera));
            header.Fields.Add(new("Directory version", raf.DirectoryVersion));

            var nodes = new List<Node> { header };

            if (raf.Jpeg.FitsIn(stream.Length))
                nodes.Add(JpegNode(stream, raf.Jpeg.Offset, raf.Jpeg.Length, "Camera preview"));

            if (raf.Metadata.FitsIn(stream.Length))
            {
                var records = ReadAt(stream, raf.Metadata.Offset, 4) is { } r ? BinaryPrimitives.ReadUInt32BigEndian(r) : 0;
                nodes.Add(new Node("Metadata records", $"{records} records", raf.Metadata.Offset, raf.Metadata.Length));
            }

            if (raf.Cfa.FitsIn(stream.Length))
            {
                var wrapped = ReadAt(stream, raf.Cfa.Offset, 2) is [(byte)'I', (byte)'I'] or [(byte)'M', (byte)'M'];
                nodes.Add(new Node("Raw image data", wrapped ? "Sensor data, in a TIFF of its own" : "Sensor data", raf.Cfa.Offset, raf.Cfa.Length));
            }

            return nodes;
        }
    }

    // Minolta MRW

    private static class MrwParts
    {
        private static readonly Dictionary<string, string> Blocks = new()
        {
            ["PRD"] = "Picture raw dimensions",
            ["TTW"] = "TIFF tags",
            ["WBG"] = "White balance gains",
            ["RIF"] = "Requested image format",
            ["PAD"] = "Padding"
        };

        public static List<Node> Describe(Stream stream)
        {
            if (MrwContainer.ReadLayout(stream) is not { } mrw)
                return [];

            var header = new Node("MRW header", "Minolta raw", 0, Math.Min(mrw.DataOffset, stream.Length));
            var nodes = new List<Node> { header };

            foreach (var block in mrw.Blocks)
                header.Children.Add(new Node(Blocks.GetValueOrDefault(block.Tag, $"'{block.Tag}' block"), $"'{block.Tag}'", block.Offset, 8L + block.DataLength));

            if (mrw.DataOffset < stream.Length)
                nodes.Add(new Node("Raw image data", null, mrw.DataOffset, stream.Length - mrw.DataOffset));

            return nodes;
        }
    }

    // Canon CRW (CIFF)

    private static class CiffParts
    {
        public static List<Node> Describe(Stream stream, byte[] head)
        {
            var little = head[0] == 'I';
            var headerLength = U32(head.AsSpan(2), little);

            return
            [
                new Node("CIFF header", "Canon CRW", 0, headerLength),
                new Node("CIFF heap", "Records and the raw data, not broken down further", headerLength, Math.Max(0, stream.Length - headerLength))
            ];
        }
    }
}