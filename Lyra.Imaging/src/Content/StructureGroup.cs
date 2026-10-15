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

    /// <summary>Where the part starts in the file, for containers whose layout is the point.</summary>
    public long? Offset { get; init; }

    /// <summary>How deep the part sits: 0 for the file's own parts, 1 for what one of them points at, and so on.</summary>
    public int Depth { get; init; }

    public IReadOnlyList<KeyValuePair<string, string>> Fields { get; init; } = [];
}