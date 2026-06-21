using System.IO.Compression;
using ZstdSharp;

namespace Lyra.ManagedCodecs.Texture.Ktx;

/// <summary>
/// Per-level decompression for KTX2 supercompression schemes. KTX2 compresses each mip level
/// independently, so a level's stored bytes are inflated to the size the level index already
/// advertises (<c>uncompressedByteLength</c>) - the readers decode into a buffer of exactly that size
/// and reject any mismatch. Zstandard uses the managed ZstdSharp port; ZLIB uses the BCL. BasisLZ is
/// not handled here (it needs a transcoder, not a stream inflate).
/// </summary>
internal static class KtxSupercompression
{
    public const uint None = 0;
    public const uint BasisLz = 1;
    public const uint Zstandard = 2;
    public const uint Zlib = 3;

    public static bool IsSupported(uint scheme) => scheme is None or Zstandard or Zlib;

    /// <summary>Inflates one supercompressed level into a fresh buffer of <paramref name="expectedSize"/> bytes.</summary>
    public static byte[] Decompress(uint scheme, ReadOnlyMemory<byte> compressed, int expectedSize) => scheme switch
    {
        Zstandard => DecompressZstd(compressed.Span, expectedSize),
        Zlib => DecompressZlib(compressed, expectedSize),
        _ => throw new NotSupportedException($"KTX2: cannot decompress supercompression scheme {scheme}."),
    };

    private static byte[] DecompressZstd(ReadOnlySpan<byte> compressed, int expectedSize)
    {
        var dst = new byte[expectedSize];
        using var decompressor = new Decompressor();

        var written = decompressor.Unwrap(compressed, dst);
        if (written != expectedSize)
            throw new InvalidDataException($"KTX2: Zstd level inflated to {written} bytes, expected {expectedSize}.");

        return dst;
    }

    private static byte[] DecompressZlib(ReadOnlyMemory<byte> compressed, int expectedSize)
    {
        var dst = new byte[expectedSize];
        using var source = new MemoryStream(compressed.ToArray(), writable: false);
        using var zlib = new ZLibStream(source, CompressionMode.Decompress);

        var total = 0;
        while (total < expectedSize)
        {
            var read = zlib.Read(dst, total, expectedSize - total);
            if (read == 0)
                break;

            total += read;
        }

        if (total != expectedSize || zlib.ReadByte() != -1)
            throw new InvalidDataException($"KTX2: ZLIB level did not inflate to exactly {expectedSize} bytes.");

        return dst;
    }
}