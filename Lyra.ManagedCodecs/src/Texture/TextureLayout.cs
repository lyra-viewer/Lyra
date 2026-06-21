namespace Lyra.ManagedCodecs.Texture;

/// <summary>
/// Container-agnostic structural facts shared by every texture reader (DDS, KTX, KTX2): the sanity
/// bound on a dimension, the full-chain mip count, and the mapping from shape flags to a
/// <see cref="TextureKind"/>.
/// </summary>
internal static class TextureLayout
{
    /// <summary>Upper bound on any single dimension; a header claiming more is treated as hostile.</summary>
    public const int MaxDimension = 65536;

    /// <summary>Largest legal mip count for the given dimensions (full chain down to 1x1x1).</summary>
    public static int MaxMipLevels(int width, int height, int depth)
    {
        var largest = Math.Max(width, Math.Max(height, depth));
        var levels = 1;
        while (largest > 1)
        {
            largest >>= 1;
            levels++;
        }

        return levels;
    }

    /// <summary>Resolves the texture kind from its shape flags. Volumes are never also arrays or cubes.</summary>
    public static TextureKind ResolveKind(bool isVolume, bool isCubemap, int arrayLayers) =>
        isVolume ? TextureKind.Volume
        : isCubemap ? TextureKind.Cube
        : arrayLayers > 1 ? TextureKind.Array2D
        : TextureKind.Texture2D;
}