namespace Lyra.ManagedCodecs.Texture;

public enum TextureKind
{
    Texture2D,
    Cube,
    Array2D,
    Volume,
}

public enum TextureOrigin
{
    TopLeft,
    BottomLeft,
}

/// <summary>
/// One decodable surface within a texture: a single mip level of a single face/layer (and, for
/// volume textures, the full depth stack of that mip). <see cref="Data"/> is usually a zero-copy view
/// into the source file (validated to lie fully within it at parse time); for supercompressed
/// containers it instead views the level's inflated buffer.
/// </summary>
public readonly struct Subresource
{
    public int MipLevel { get; init; }
    public int ArrayLayer { get; init; }
    public int Face { get; init; }
    public int Width { get; init; }
    public int Height { get; init; }
    public int Depth { get; init; }

    public ReadOnlyMemory<byte> Data { get; init; }
}

/// <summary>
/// A parsed texture container (DDS, KTX, …): its format identity, shape, and the list of
/// subresources pointing back into the source file. Parsing never decodes - decoding is opt-in via
/// <see cref="Decode"/>, so the fast path (e.g. a future GPU upload) can pass <see cref="Subresource.Data"/>
/// through untouched while thumbnails / perceptual hashing / display decode only what they need.
/// </summary>
public sealed class TextureData
{
    public required TextureFormat Format { get; init; }

    /// <summary>The format's name as it appeared in the source container (e.g. "BC7_UNORM", "DXT4").</summary>
    public required string FormatName { get; init; }
    public required TextureKind Kind { get; init; }
    public required int Width { get; init; }
    public required int Height { get; init; }
    public required int Depth { get; init; }
    public required int MipLevels { get; init; }
    public required int ArrayLayers { get; init; }

    /// <summary>
    /// Corner of the first stored row. Defaults to <see cref="TextureOrigin.TopLeft"/> (DDS, KTX2);
    /// readers that expose bottom-up data (KTX 1.x) set <see cref="TextureOrigin.BottomLeft"/> so the
    /// display path knows to flip vertically after decoding.
    /// </summary>
    public TextureOrigin Origin { get; init; } = TextureOrigin.TopLeft;

    public required IReadOnlyList<Subresource> Subresources { get; init; }

    /// <summary>Bytes needed to decode <paramref name="sr"/> into RGBA8 (<c>Width * Height * 4</c>).</summary>
    public static long DecodedByteSize(in Subresource sr) => checked((long)sr.Width * sr.Height * 4);

    /// <summary>True if <see cref="Format"/> is scene-referred HDR and must be decoded via <see cref="DecodeHdr"/>.</summary>
    public bool IsHdr => TextureFormats.Info(Format).IsHdr;

    /// <summary>Decodes one subresource into <paramref name="dst"/> as 8-bit RGBA, top-left origin.</summary>
    public void Decode(in Subresource sr, Span<byte> dst)
        => SurfaceDecoder.DecodeSurface(Format, sr.Data.Span, dst, sr.Width, sr.Height);

    /// <summary>Decodes one HDR subresource into <paramref name="dst"/> as linear RGBA float.</summary>
    public void DecodeHdr(in Subresource sr, Span<float> dst)
        => SurfaceDecoder.DecodeSurfaceHdr(Format, sr.Data.Span, dst, sr.Width, sr.Height);
}
