namespace Lyra.Imaging.Content.Textures;

public enum TextureLocation
{
    Embedded,
    Linked,
    FoundByName,
    Missing,
    Broken
}

/// <param name="StoredPath">The path as the model stores it, for a texture outside the model.</param>
/// <param name="ResolvedPath">The file Lyra read it from, when found.</param>
public sealed record TextureSource(TextureLocation Location, string? StoredPath, string? ResolvedPath)
{
    public bool IsUnavailable => Location is TextureLocation.Missing or TextureLocation.Broken;
}
