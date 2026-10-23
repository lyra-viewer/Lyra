using System.Buffers.Binary;
using Lyra.Metadata.Support;

namespace Lyra.Metadata.Containers;

public sealed record BmpHeader(int? HeaderSize, int? BitsPerPixel, int? Compression);

/// <summary>A BMP's headers, more than one in an OS/2 bitmap array.</summary>
public sealed record BmpFacts(IReadOnlyList<BmpHeader> Headers) : IFormatFacts;

public sealed class BmpContainer : IBufferedContainer
{
    private const ushort BitmapArray = 0x4142; // "BA"
    private static readonly ushort[] Headers = [0x4D42, 0x4943, 0x5043, 0x4349, 0x5450]; // "BM", "CI", "CP", "IC", "PT"

    private const uint LinkedProfile = 0x4C494E4B;   // "LINK", read as a little-endian number
    private const uint EmbeddedProfile = 0x4D424544; // "MBED"

    private const int MaxArrayEntries = 4096;

    public static bool Recognizes(ReadOnlySpan<byte> head) =>
        head.Length >= 2 && BinaryPrimitives.ReadUInt16LittleEndian(head) is var magic && (magic == BitmapArray || Headers.Contains(magic));

    public static ContainerContents? Read(ReadOnlySpan<byte> data)
    {
        if (!Recognizes(data))
            return null;

        var container = new ContainerContents();
        var headers = new List<BmpHeader>();
        var reader = new ByteReader(data, littleEndian: true);

        for (var entry = 0; entry < MaxArrayEntries && reader.UInt16() is { } magic; entry++)
        {
            if (magic != BitmapArray)
            {
                if (Headers.Contains(magic))
                    ReadHeader(ref reader, container, headers);

                break;
            }

            reader.Skip(4);
            var next = reader.UInt32();
            reader.Skip(4);

            if (reader.UInt16() is { } nested && Headers.Contains(nested))
                ReadHeader(ref reader, container, headers);

            if (!reader.Ok || next is not { } at || at == 0 || reader.Position > at)
                break;

            reader.Seek(at);
        }

        if (headers.Count > 0)
            container.AddFacts(new BmpFacts(headers));

        container.Damaged = !reader.Ok;
        return container;
    }

    private static void ReadHeader(ref ByteReader reader, ContainerContents container, List<BmpHeader> headers)
    {
        reader.Skip(12);
        var start = reader.Position;
        var size = reader.Int32();
        int? bits = null;
        int? compression = null;

        switch (size)
        {
            case 12:
                reader.Skip(6);
                bits = reader.UInt16();
                break;

            case 16 or 64:
                reader.Skip(10);
                bits = reader.UInt16();
                if (size == 64)
                {
                    compression = reader.Int32();
                    reader.Skip(44);
                }
                break;

            case 40 or 52 or 56 or 108:
                reader.Skip(10);
                bits = reader.UInt16();
                compression = reader.Int32();
                reader.Skip(size.Value - 20);
                break;

            case 124:
                reader.Skip(10);
                bits = reader.UInt16();
                compression = reader.Int32();
                ReadProfile(ref reader, start, container);
                break;
        }

        headers.Add(new BmpHeader(size, bits, compression));
    }
    
    private static void ReadProfile(ref ByteReader reader, int start, ContainerContents container)
    {
        reader.Skip(36);
        var colorSpace = reader.UInt32();
        reader.Skip(52);
        if (colorSpace is not (LinkedProfile or EmbeddedProfile))
        {
            reader.Skip(12);
            return;
        }

        if (reader.UInt32() is not { } offset || reader.Int32() is not { } length || reader.Position > start + (long)offset || length < 0)
            return;

        reader.Seek(start + (long)offset);
        if (reader.Bytes(length) is { } profile && colorSpace == EmbeddedProfile)
            container.Add(MetadataBlockKind.Icc, profile);
    }
}