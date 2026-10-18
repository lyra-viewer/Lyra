using System.IO.Compression;

namespace Lyra.Metadata.Support;

internal static class Decompression
{
    public static byte[]? Zlib(ReadOnlySpan<byte> compressed, long maxBytes) =>
        Unpack(compressed, maxBytes, input => new ZLibStream(input, CompressionMode.Decompress));

    public static byte[]? Brotli(ReadOnlySpan<byte> compressed, long maxBytes) =>
        Unpack(compressed, maxBytes, input => new BrotliStream(input, CompressionMode.Decompress));

    private static byte[]? Unpack(ReadOnlySpan<byte> compressed, long maxBytes, Func<Stream, Stream> decompressor)
    {
        try
        {
            using var input = new MemoryStream(compressed.ToArray(), writable: false);
            using var decompressed = decompressor(input);
            using var output = new MemoryStream();

            var buffer = new byte[81920];
            int read;
            while ((read = decompressed.Read(buffer)) > 0)
            {
                if (output.Length + read > maxBytes)
                    return null;

                output.Write(buffer, 0, read);
            }

            return output.ToArray();
        }
        catch (InvalidDataException)
        {
            return null;
        }
    }
}