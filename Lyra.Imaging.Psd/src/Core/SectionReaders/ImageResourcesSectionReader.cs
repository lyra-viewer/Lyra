using Lyra.Imaging.Psd.Core.Primitives;
using Lyra.Imaging.Psd.Core.Readers;
using Lyra.Imaging.Psd.Core.SectionData;

namespace Lyra.Imaging.Psd.Core.SectionReaders;

internal static class ImageResourcesSectionReader
{
    public static ImageResources Read(PsdBigEndianReader reader)
    {
        var length = reader.ReadUInt32();

        var start = reader.Position;
        var end = start + length;

        var blocks = new List<ImageResourceBlockHeader>();

        while (reader.Position < end)
        {
            reader.ExpectSignature(PsdSignatures.ImageResources);

            var id = reader.ReadUInt16();
            var name = reader.ReadPascalString(2);
            var dataSize = reader.ReadUInt32();
            var dataOffset = reader.Position;

            if (reader.CanSeek)
            {
                var remaining = end - reader.Position;
                if (dataSize > remaining)
                    throw new InvalidDataException($"Image resource block {id} exceeds section bounds.");
            }

            reader.Skip(dataSize);
            if ((dataSize & 1) == 1)
                reader.Skip(1);

            blocks.Add(new ImageResourceBlockHeader(id, name, dataSize, dataOffset));
        }

        return reader.Position == end
            ? new ImageResources(length, blocks.ToArray())
            : throw new InvalidDataException("ImageResourcesSection misaligned.");
    }
}