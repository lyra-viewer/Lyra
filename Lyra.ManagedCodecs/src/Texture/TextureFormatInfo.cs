namespace Lyra.ManagedCodecs.Texture;

/// <summary>
/// Structural facts about a <see cref="TextureFormat"/> needed to size and slice surfaces.
/// For uncompressed formats the "block" is a single 1x1 pixel, so <see cref="BytesPerBlock"/>
/// doubles as bytes-per-pixel.
/// </summary>
public readonly struct TextureFormatInfo
{
    public bool IsCompressed { get; init; }
    public int BlockWidth { get; init; }
    public int BlockHeight { get; init; }
    public int BytesPerBlock { get; init; }
    public bool IsSrgb { get; init; }
    public bool IsSigned { get; init; }
    public bool IsHdr { get; init; }
    public bool HasAlpha { get; init; }

    public int BitsPerPixel => BytesPerBlock * 8 / (BlockWidth * BlockHeight);
}
