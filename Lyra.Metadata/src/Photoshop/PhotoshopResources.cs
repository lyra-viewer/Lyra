using System.Buffers.Binary;
using System.Text;

namespace Lyra.Metadata.Photoshop;

public sealed record PhotoshopResource(ushort Id, string Name, byte[] Data);

/// <summary>
/// Photoshop image resource blocks ("8BIM"), as a TIFF's tag 0x8649, a JPEG's APP13 and a PSD's
/// resource section carry them. Reading stops at the first block that is not one and keeps the
/// blocks before it.
/// </summary>
public sealed class PhotoshopResources : IMetadataBlock<PhotoshopResources>
{
    public const ushort Iptc = 0x0404;
    public const ushort IccProfile = 0x040F;
    public const ushort Exif = 0x0422;
    public const ushort ExifData3 = 0x0423;
    public const ushort Xmp = 0x0424;

    private const int MaxResources = 4096;

    public IReadOnlyList<PhotoshopResource> Resources { get; }

    private PhotoshopResources(IReadOnlyList<PhotoshopResource> resources) => Resources = resources;

    public static MetadataBlockKind? BlockKind(ushort id) => id switch
    {
        Iptc => MetadataBlockKind.Iptc,
        Exif or ExifData3 => MetadataBlockKind.Exif,
        Xmp => MetadataBlockKind.Xmp,
        IccProfile => MetadataBlockKind.Icc,
        _ => null
    };

    public byte[]? Find(ushort id) => Resources.FirstOrDefault(resource => resource.Id == id)?.Data;

    public static PhotoshopResources Parse(ReadOnlySpan<byte> data)
    {
        var resources = new List<PhotoshopResource>();
        var offset = 0;

        while (offset + 12 <= data.Length && resources.Count < MaxResources && IsSignature(data.Slice(offset, 4)))
        {
            var id = BinaryPrimitives.ReadUInt16BigEndian(data[(offset + 4)..]);
            offset += 6;

            var nameLength = data[offset];
            var nameField = nameLength + 1 + (nameLength + 1) % 2;
            if (offset + nameField + 4 > data.Length)
                break;

            var name = Encoding.Latin1.GetString(data.Slice(offset + 1, nameLength));
            offset += nameField;

            var size = BinaryPrimitives.ReadUInt32BigEndian(data[offset..]);
            offset += 4;
            if (size > data.Length - offset)
                break;

            resources.Add(new PhotoshopResource(id, name, data.Slice(offset, (int)size).ToArray()));
            offset += (int)size + (int)(size % 2);
        }

        return new PhotoshopResources(resources);
    }

    private static bool IsSignature(ReadOnlySpan<byte> sig) =>
        sig.SequenceEqual("8BIM"u8) || sig.SequenceEqual("MeSa"u8) || sig.SequenceEqual("PHUT"u8) || sig.SequenceEqual("AgHg"u8) || sig.SequenceEqual("DCSR"u8);
}