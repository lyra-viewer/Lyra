namespace Lyra.ManagedCodecs.Scene;

public enum SceneFormat
{
    Gltf,
    Glb,
    Fbx
}

/// <summary>
/// The textures a 3D model uses, as materials with named slots pointing at images. An image used by
/// several slots is the same <see cref="SceneImage"/> instance in each.
/// </summary>
public sealed class SceneTextures
{
    public required SceneFormat Format { get; init; }

    /// <summary>The tool that wrote the file, as the file names it.</summary>
    public string? Generator { get; init; }

    public required IReadOnlyList<SceneMaterial> Materials { get; init; }

    /// <summary>Every image in the file, in file order, whether a material uses it or not.</summary>
    public required IReadOnlyList<SceneImage> Images { get; init; }

    public IReadOnlyList<string> Issues { get; init; } = [];

    /// <summary>Images no material slot uses, not even as an alternate.</summary>
    public IReadOnlyList<SceneImage> Unreferenced
    {
        get
        {
            var used = new HashSet<SceneImage>(ReferenceEqualityComparer.Instance);

            foreach (var slot in Materials.SelectMany(material => material.Slots))
            {
                used.Add(slot.Image);
                used.UnionWith(slot.Alternates);
            }

            return [.. Images.Where(image => !used.Contains(image))];
        }
    }
}
