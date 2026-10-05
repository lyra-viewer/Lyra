using System.Buffers.Binary;

namespace Lyra.ManagedCodecs.Raster.Png;

/// <summary>
/// Turns one APNG frame into a PNG of its own, which any PNG decoder can then decode: the
/// frame's size in IHDR, the file's shared chunks, and the frame's data as a single IDAT.
/// </summary>
public static class ApngFramePng
{
    private static ReadOnlySpan<byte> End => [0, 0, 0, 0, 0x49, 0x45, 0x4E, 0x44, 0xAE, 0x42, 0x60, 0x82];

    public static byte[] Build(Stream source, ApngChunks chunks, ApngFrame frame)
    {
        var shared = chunks.SharedChunks.Sum(c => (long)c.Length);
        var data = frame.Data.Sum(d => (long)d.Length);
        var total = 8 + (12 + 13) + shared + 12 + data + End.Length;

        if (total > Array.MaxLength)
            throw new InvalidDataException($"An APNG frame of {data} bytes is too large to decode.");

        var png = new byte[total];
        var span = png.AsSpan();

        ApngChunkReader.Signature.CopyTo(span);
        var pos = 8;

        Span<byte> ihdr = stackalloc byte[13];
        BinaryPrimitives.WriteInt32BigEndian(ihdr, frame.Width);
        BinaryPrimitives.WriteInt32BigEndian(ihdr[4..], frame.Height);
        ihdr[8] = chunks.BitDepth;
        ihdr[9] = chunks.ColorType;
        ihdr[12] = chunks.Interlace;
        pos += WriteChunk(span[pos..], "IHDR"u8, ihdr);

        foreach (var chunk in chunks.SharedChunks)
        {
            ReadAt(source, chunk.Offset, span.Slice(pos, chunk.Length));
            pos += chunk.Length;
        }

        BinaryPrimitives.WriteInt32BigEndian(span[pos..], (int)data);
        "IDAT"u8.CopyTo(span[(pos + 4)..]);

        var idat = pos + 8;
        foreach (var segment in frame.Data)
        {
            ReadAt(source, segment.Offset, span.Slice(idat, segment.Length));
            idat += segment.Length;
        }

        BinaryPrimitives.WriteUInt32BigEndian(span[idat..], PngCrc.Compute(span[(pos + 4)..idat]));
        pos = idat + 4;

        End.CopyTo(span[pos..]);
        return png;
    }

    private static int WriteChunk(Span<byte> output, ReadOnlySpan<byte> type, ReadOnlySpan<byte> data)
    {
        BinaryPrimitives.WriteInt32BigEndian(output, data.Length);
        type.CopyTo(output[4..]);
        data.CopyTo(output[8..]);
        BinaryPrimitives.WriteUInt32BigEndian(output[(8 + data.Length)..], PngCrc.Compute(output.Slice(4, 4 + data.Length)));
        return 12 + data.Length;
    }

    private static void ReadAt(Stream source, long offset, Span<byte> destination)
    {
        source.Position = offset;
        source.ReadExactly(destination);
    }
}