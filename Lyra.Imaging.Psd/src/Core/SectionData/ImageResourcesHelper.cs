using Lyra.Imaging.Psd.Core.Readers;

namespace Lyra.Imaging.Psd.Core.SectionData;

public static class ImageResourcesHelper
{
    public static bool TryGetResourceBlock(in ImageResources resources, ushort resourceId, out ImageResourceBlockHeader header)
    {
        // Avoid LINQ
        var blocks = resources.Blocks;
        for (var i = 0; i < blocks.Length; i++)
        {
            var b = blocks[i];
            if (b.Id == resourceId)
            {
                header = b;
                return true;
            }
        }

        header = default;
        return false;
    }

    public static byte[] ReadResourceBytes(PsdBigEndianReader reader, in ImageResourceBlockHeader header)
    {
        ArgumentNullException.ThrowIfNull(reader);

        var pos = reader.Position;
        try
        {
            reader.Position = header.DataOffset;

            if (header.DataSize == 0)
                return [];

            if (header.DataSize > int.MaxValue)
                throw new NotSupportedException($"Image resource block {header.Id} size {header.DataSize} exceeds supported limits.");

            var data = new byte[(int)header.DataSize];
            reader.ReadExactly(data);
            return data;
        }
        finally
        {
            reader.Position = pos;
        }
    }
}