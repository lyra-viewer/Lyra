using System.Buffers.Binary;
using System.Text;

namespace Lyra.Imaging.Decoding.Structure;

internal static partial class RawStructure
{
    // Fujifilm RAF

    private static class RafParts
    {
        private readonly record struct Block(long At, long Length);

        public static List<Node> Describe(Stream stream)
        {
            if (ReadAt(stream, 0, 108) is not { } head)
                return [];

            uint U32(int at) => BinaryPrimitives.ReadUInt32BigEndian(head.AsSpan(at));

            var camera = Encoding.ASCII.GetString(head, 28, 32).Split('\0')[0].Trim();
            var header = new Node("RAF header", $"Fujifilm {camera}".Trim(), 0, 84 + 24);
            header.Fields.Add(new("Format version", Encoding.ASCII.GetString(head, 16, 4)));
            header.Fields.Add(new("Camera", camera));
            header.Fields.Add(new("Directory version", Encoding.ASCII.GetString(head, 60, 4)));

            var nodes = new List<Node> { header };

            Block jpeg = new(U32(84), U32(88)), meta = new(U32(92), U32(96)), cfa = new(U32(100), U32(104));

            if (jpeg.At > 0 && jpeg.At + jpeg.Length <= stream.Length)
                nodes.Add(JpegNode(stream, jpeg.At, jpeg.Length, "Camera preview"));

            if (meta.At > 0 && meta.At + meta.Length <= stream.Length)
            {
                var records = ReadAt(stream, meta.At, 4) is { } r ? BinaryPrimitives.ReadUInt32BigEndian(r) : 0;
                nodes.Add(new Node("Metadata records", $"{records} records", meta.At, meta.Length));
            }

            if (cfa.At > 0 && cfa.At + cfa.Length <= stream.Length)
            {
                var wrapped = ReadAt(stream, cfa.At, 2) is [(byte)'I', (byte)'I'] or [(byte)'M', (byte)'M'];
                nodes.Add(new Node("Raw image data", wrapped ? "Sensor data, in a TIFF of its own" : "Sensor data", cfa.At, cfa.Length));
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
            if (ReadAt(stream, 4, 4) is not { } lengthBytes)
                return [];

            var dataAt = 8L + BinaryPrimitives.ReadUInt32BigEndian(lengthBytes);
            var header = new Node("MRW header", "Minolta raw", 0, Math.Min(dataAt, stream.Length));
            var nodes = new List<Node> { header };

            var offset = 8L;
            for (var i = 0; offset + 8 <= dataAt && i < 64; i++)
            {
                if (ReadAt(stream, offset, 8) is not { } block)
                    break;

                var tag = Encoding.ASCII.GetString(block, 1, 3);
                var length = BinaryPrimitives.ReadUInt32BigEndian(block.AsSpan(4));
                header.Children.Add(new Node(Blocks.GetValueOrDefault(tag, $"'{tag}' block"), $"'{tag}'", offset, 8L + length));
                offset += 8L + length;
            }

            if (dataAt < stream.Length)
                nodes.Add(new Node("Raw image data", null, dataAt, stream.Length - dataAt));

            return nodes;
        }
    }

    // Canon CRW (CIFF)

    private static class CiffParts
    {
        public static List<Node> Describe(Stream stream, byte[] head)
        {
            var little = head[0] == 'I';
            var headerLength = little ? BinaryPrimitives.ReadUInt32LittleEndian(head.AsSpan(2)) : BinaryPrimitives.ReadUInt32BigEndian(head.AsSpan(2));

            return
            [
                new Node("CIFF header", "Canon CRW", 0, headerLength),
                new Node("CIFF heap", "Records and the raw data, not broken down further", headerLength, Math.Max(0, stream.Length - headerLength))
            ];
        }
    }
}