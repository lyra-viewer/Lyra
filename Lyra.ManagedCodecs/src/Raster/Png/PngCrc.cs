namespace Lyra.ManagedCodecs.Raster.Png;

/// <summary>The CRC-32 PNG chunks carry, over the chunk type and data.</summary>
public static class PngCrc
{
    private static readonly uint[] Table = BuildTable();

    public static uint Compute(ReadOnlySpan<byte> data)
    {
        var crc = 0xFFFFFFFFu;

        foreach (var b in data)
            crc = Table[(crc ^ b) & 0xFF] ^ (crc >> 8);

        return ~crc;
    }

    private static uint[] BuildTable()
    {
        var table = new uint[256];

        for (var n = 0u; n < 256; n++)
        {
            var c = n;
            for (var k = 0; k < 8; k++)
                c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;

            table[n] = c;
        }

        return table;
    }
}
