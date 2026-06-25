namespace Lyra.Imaging.Content;

/// <summary>
/// One named part of a file's structure for the inspector view: a header section, a sub-structure,
/// a mip level, etc. Decoders that understand their container's layout (DDS, KTX, PSD) emit
/// an ordered list of these on <see cref="Composite.Structure"/>; the UI renders each as a
/// collapsible panel whose body is the <see cref="Fields"/> key-value list.
/// </summary>
public sealed class StructureGroup
{
    public required string Name { get; init; }

    public string? Description { get; init; }

    public long? SizeBytes { get; init; }

    public IReadOnlyList<KeyValuePair<string, string>> Fields { get; init; } = [];
}