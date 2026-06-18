namespace Lyra.ManagedCodecs;

/// <summary>
/// A pure-managed single-image reader: parses encoded bytes into raw pixels with no dependency on
/// the imaging pipeline or SkiaSharp. Implementations are stateless and thread-safe.
/// </summary>
public interface IManagedImageReader
{
    /// <summary>
    /// Sniffs whether <paramref name="header"/> (the first bytes of a file) looks like this
    /// decoder's format. Cheap, allocation-free; never reads the whole file.
    /// </summary>
    bool CanDecode(ReadOnlySpan<byte> header);

    /// <summary>
    /// Decodes <paramref name="stream"/> into an 8-bit RGBA image with top-left origin.
    /// Throws <see cref="System.IO.InvalidDataException"/> for malformed input and
    /// <see cref="System.NotSupportedException"/> for valid-but-unsupported variants.
    /// </summary>
    DecodedImage Decode(Stream stream);
}
