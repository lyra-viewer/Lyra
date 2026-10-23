using System.Buffers.Binary;

namespace Lyra.Metadata.Containers;

public sealed record TgaFacts(int ImageDepth) : IFormatFacts;

public sealed class TgaContainer : IBufferedContainer
{
    private const int HeaderLength = 18;
    
    public static TgaFacts? ReadHeader(ReadOnlySpan<byte> head)
    {
        if (head.Length < HeaderLength)
            return null;

        var colormapType = head[1];
        var dataType = head[2];

        var validType = dataType switch
        {
            1 or 9 => colormapType != 0,
            2 or 10 or 3 or 11 => colormapType == 0,
            _ => false
        };

        if (!validType || colormapType > 1)
            return null;

        if (colormapType == 1)
        {
            var origin = BinaryPrimitives.ReadInt16LittleEndian(head[3..]);
            var length = BinaryPrimitives.ReadInt16LittleEndian(head[5..]);
            if (origin < 0 || length < 0 || head[7] is not (15 or 16 or 24 or 32))
                return null;
        }

        if (BinaryPrimitives.ReadInt16LittleEndian(head[12..]) <= 0 || BinaryPrimitives.ReadInt16LittleEndian(head[14..]) <= 0)
            return null;

        var depth = head[16];
        var validDepth = depth switch
        {
            8 => dataType is 1 or 3 or 9 or 11,
            16 => true,
            24 or 32 => dataType is 2 or 10,
            _ => false
        };

        var flags = head[17];
        var attributeBits = flags & 0xF;
        if (!validDepth || (flags & 0xC0) != 0 || attributeBits is not (0 or 6 or 8))
            return null;

        return new TgaFacts(depth);
    }

    public static bool Recognizes(ReadOnlySpan<byte> head) => ReadHeader(head) is not null;

    public static ContainerContents? Read(ReadOnlySpan<byte> data)
    {
        if (ReadHeader(data) is not { } facts)
            return null;

        var container = new ContainerContents();
        container.AddFacts(facts);
        return container;
    }
}