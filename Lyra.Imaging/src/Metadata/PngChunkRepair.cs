using System.Buffers.Binary;
using MetadataExtractor.Formats.Png;

namespace Lyra.Imaging.Metadata;

/// <summary>
/// MetadataExtractor rejects a whole PNG over two faults that leave its metadata intact: a chunk
/// the specification allows once written twice, and a file cut short. This rebuilds the file with
/// the first of each chunk, ending at the last complete one, and without the pixel data, which no
/// metadata comes from.
/// </summary>
internal static class PngChunkRepair
{
    private static ReadOnlySpan<byte> Signature => [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    private static ReadOnlySpan<byte> End => [0, 0, 0, 0, 0x49, 0x45, 0x4E, 0x44, 0xAE, 0x42, 0x60, 0x82];
    
    public static MemoryStream? Repair(Stream stream, out string repair)
    {
        repair = string.Empty;
        Span<byte> header = stackalloc byte[8];

        if (stream.ReadAtLeast(header, 8, throwOnEndOfStream: false) < 8 || !header.SequenceEqual(Signature))
            return null;

        var output = new MemoryStream();
        output.Write(header);

        var seen = new HashSet<PngChunkType>();
        var repeated = false;
        var ended = false;
        var sawHeader = false;

        while (stream.ReadAtLeast(header, 8, throwOnEndOfStream: false) == 8)
        {
            var length = BinaryPrimitives.ReadUInt32BigEndian(header);

            if (!TryChunkType(header[4..], out var type) || length > int.MaxValue - 4)
                return null;

            // Cut short inside this chunk: keep what came before it.
            if (length + 4L > stream.Length - stream.Position)
                break;

            var repeat = !type.AreMultipleAllowed && !seen.Add(type);
            if (repeat || type.Identifier == "IDAT")
            {
                repeated |= repeat;
                stream.Seek(length + 4, SeekOrigin.Current); // data and CRC
            }
            else
            {
                var body = new byte[length + 4];
                if (stream.ReadAtLeast(body, body.Length, throwOnEndOfStream: false) < body.Length)
                    break;

                output.Write(header);
                output.Write(body);

                sawHeader |= type.Identifier == "IHDR";
            }

            if (type.Identifier == "IEND")
            {
                ended = true;
                break;
            }
        }

        if (!sawHeader || (ended && !repeated))
            return null;

        if (!ended)
            output.Write(End);

        repair = (repeated, ended) switch
        {
            (true, true) => "repeated chunks",
            (true, false) => "repeated chunks, cut short",
            _ => "cut short"
        };

        output.Position = 0;
        return output;
    }

    /// <summary>False for bytes that are not a chunk type.</summary>
    private static bool TryChunkType(ReadOnlySpan<byte> bytes, out PngChunkType type)
    {
        try
        {
            type = new PngChunkType(bytes.ToArray());
            return true;
        }
        catch (ArgumentException)
        {
            type = default;
            return false;
        }
    }
}
