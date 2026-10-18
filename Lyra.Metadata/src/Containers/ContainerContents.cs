using Lyra.Metadata.Photoshop;
using Lyra.Metadata.Tiff;

namespace Lyra.Metadata.Containers;

public sealed record MetadataBlock(MetadataBlockKind Kind, byte[] Data, TiffDirectoryKind FirstDirectory = TiffDirectoryKind.Image);

public interface IFormatFacts;

public sealed class ContainerContents
{
    private readonly List<MetadataBlock> _blocks = [];
    private readonly List<IFormatFacts> _facts = [];

    public IReadOnlyList<MetadataBlock> Blocks => _blocks;

    public IReadOnlyList<IFormatFacts> Facts => _facts;

    public bool Damaged { get; internal set; }

    public bool DamageWithNothingFails { get; internal init; }

    public bool Failed => Damaged && IsEmpty && DamageWithNothingFails;

    public bool IsEmpty => _blocks.Count == 0 && _facts.Count == 0;

    public T? Fact<T>() where T : class, IFormatFacts => _facts.OfType<T>().FirstOrDefault();

    public static ContainerContents FromPhotoshop(IEnumerable<PhotoshopResource> resources)
    {
        var container = new ContainerContents();
        container.AddPhotoshop(resources);
        return container;
    }

    internal void Add(MetadataBlockKind kind, byte[] data) => _blocks.Add(new MetadataBlock(kind, data));

    internal void AddIfPresent(MetadataBlockKind kind, byte[]? data)
    {
        if (data is not null)
            Add(kind, data);
    }

    internal void AddExif(byte[] tiff, TiffDirectoryKind firstDirectory) => _blocks.Add(new MetadataBlock(MetadataBlockKind.Exif, tiff, firstDirectory));

    internal void AddFacts(IFormatFacts facts) => _facts.Add(facts);

    internal void AddPhotoshop(IEnumerable<PhotoshopResource> resources)
    {
        foreach (var resource in resources)
            if (PhotoshopResources.BlockKind(resource.Id) is { } kind)
                Add(kind, resource.Data);
    }
}