using System.Buffers.Binary;

namespace Lyra.Metadata.Tests.Support;

/// <summary>Builds MRW files: the header, its blocks with the TTW TIFF last, then some raw data.</summary>
internal static class MrwFixture
{
    public static byte[] Build(byte[] tiff, byte[][]? leading = null)
    {
        byte[][] blocks = [.. leading ?? [], Block("TTW", tiff)];
        var length = blocks.Sum(block => block.Length);

        var header = new byte[8];
        "\0MRM"u8.CopyTo(header);
        BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(4), (uint)length);

        return [.. header, .. blocks.SelectMany(block => block), .. new byte[64]];
    }

    public static byte[] Block(string tag, byte[] data)
    {
        var header = new byte[8];
        header[1] = (byte)tag[0];
        header[2] = (byte)tag[1];
        header[3] = (byte)tag[2];
        BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(4), (uint)data.Length);

        return [.. header, .. data];
    }
}