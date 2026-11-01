using System.Diagnostics;

namespace Lyra.Imaging.Content.Textures;

/// <param name="Texture">The texture the slot shows, as an index into the variant set.</param>
/// <param name="Alternates">Other textures offered for the same slot.</param>
public sealed record TextureSlot(string Label, int Texture, IReadOnlyList<int> Alternates);

public sealed record TextureMaterial(string Name, IReadOnlyList<TextureSlot> Slots)
{
    /// <summary>The variant showing the material's textures side by side, when it has more than one.</summary>
    public int? Sheet { get; init; }
}

/// <summary>
/// How a 3D model's textures are organised. Each texture is one variant, however many slots use
/// it, so it is decoded once.
/// </summary>
/// <param name="Unreferenced">Textures no material uses.</param>
/// <param name="Sources">Where each texture comes from, by variant index.</param>
[DebuggerDisplay("{Materials.Count} materials, {Sources.Count} textures, overview {Overview}")]
public sealed record TextureLayout(IReadOnlyList<TextureMaterial> Materials, IReadOnlyList<int> Unreferenced, IReadOnlyList<TextureSource> Sources)
{
    /// <summary>The variant showing each material's first texture side by side, when there are several materials.</summary>
    public int? Overview { get; init; }

    /// <summary>Where the texture at a variant index comes from; null for a contact sheet.</summary>
    public TextureSource? SourceOf(int index) => index >= 0 && index < Sources.Count ? Sources[index] : null;

    /// <summary>Every texture in the order the layout lists it, each once: slots, their alternates, then unreferenced.</summary>
    public IEnumerable<int> InListOrder() =>
        Materials
            .SelectMany(material => material.Slots)
            .SelectMany(slot => slot.Alternates.Prepend(slot.Texture))
            .Concat(Unreferenced)
            .Distinct();

    /// <summary>The textures in list order that can be shown, leaving out the missing and the broken.</summary>
    public IReadOnlyList<int> ShowableInListOrder() => [.. InListOrder().Where(index => SourceOf(index) is { IsUnavailable: false })];
}