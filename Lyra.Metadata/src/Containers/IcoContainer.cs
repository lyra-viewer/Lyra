using Lyra.Metadata.Support;

namespace Lyra.Metadata.Containers;

public sealed record IcoEntry(int? Width, int? Height, int? BitsPerPixel)
{
    public int Pixels => (Width ?? 256) * (Height ?? 256);
}

/// <summary>An icon's directory entries, in file order.</summary>
public sealed record IcoFacts(IReadOnlyList<IcoEntry> Entries) : IFormatFacts
{
    /// <summary>The entry an icon opens on: the largest, the first of equals.</summary>
    public IcoEntry? Largest => Entries.OrderByDescending(entry => entry.Pixels).FirstOrDefault();
}

public sealed class IcoContainer : IBufferedContainer
{
    public static bool Recognizes(ReadOnlySpan<byte> head) => head.StartsWith((ReadOnlySpan<byte>)[0, 0, 1, 0]);

    public static ContainerContents? Read(ReadOnlySpan<byte> data)
    {
        if (!Recognizes(data))
            return null;

        var container = new ContainerContents();
        var reader = new ByteReader(data, littleEndian: true);
        reader.Skip(4);

        var entries = new List<IcoEntry>();
        var count = reader.UInt16() ?? 0;
        for (var i = 0; i < count && reader.Ok; i++)
        {
            // Both sides or neither: an entry cut between them has no size, and counts as the largest.
            var width = reader.UInt8();
            var height = reader.UInt8();
            reader.Skip(4);
            var bits = reader.UInt16();
            reader.Skip(8);

            var sized = width is not null && height is not null;
            entries.Add(new IcoEntry(sized ? Side(width) : null, sized ? Side(height) : null, bits));
        }

        if (entries.Count > 0)
            container.AddFacts(new IcoFacts(entries));

        container.Damaged = !reader.Ok;
        return container;
    }

    private static int? Side(byte? value) => value is { } side ? side == 0 ? 256 : side : null;
}