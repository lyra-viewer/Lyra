namespace Lyra.ManagedCodecs.Scene;

public sealed record SceneMaterial(int Index, string? Name, IReadOnlyList<MaterialSlot> Slots);

/// <param name="Key">Where the slot sits in the file, such as <c>pbrMetallicRoughness.baseColorTexture</c>.</param>
/// <param name="Label">The slot's readable name, such as <c>Base Color</c>.</param>
/// <param name="Alternates">Other images the same texture offers, such as the PNG fallback of a KTX2 texture.</param>
public sealed record MaterialSlot(string Key, string Label, SceneImage Image, IReadOnlyList<SceneImage> Alternates);