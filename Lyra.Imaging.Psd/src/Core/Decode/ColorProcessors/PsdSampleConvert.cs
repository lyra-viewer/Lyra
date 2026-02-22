using System.Buffers.Binary;

namespace Lyra.Imaging.Psd.Core.Decode.ColorProcessors;

internal static class PsdSampleConvert
{
    private static byte U16To8(ushort v) => (byte)((v + 128) / 257);

    public static void Row16BeTo8(Span<byte> dst8, ReadOnlySpan<byte> src16be)
    {
        for (var i = 0; i < dst8.Length; i++)
        {
            var v = BinaryPrimitives.ReadUInt16BigEndian(src16be.Slice(i * 2, 2));
            dst8[i] = U16To8(v);
        }
    }

    public static void Row32FloatBeTo8(Span<byte> dst8, ReadOnlySpan<byte> src32be)
    {
        for (var i = 0; i < dst8.Length; i++)
        {
            var bits = BinaryPrimitives.ReadUInt32BigEndian(src32be.Slice(i * 4, 4));
            var f = BitConverter.Int32BitsToSingle(unchecked((int)bits));
            dst8[i] = Float01To8(f);
        }
    }

    public static byte SampleTo8(ReadOnlySpan<byte> row, int offsetBytes, int bpcBytes)
    {
        return bpcBytes switch
        {
            1 => row[offsetBytes],
            2 => U16To8(BinaryPrimitives.ReadUInt16BigEndian(row.Slice(offsetBytes, 2))),
            4 => Float01To8(BitConverter.Int32BitsToSingle(unchecked((int)BinaryPrimitives.ReadUInt32BigEndian(row.Slice(offsetBytes, 4))))),
            _ => throw new NotSupportedException($"Unsupported bytes-per-channel: {bpcBytes}.")
        };
    }

    public static byte Float01To8(float f)
    {
        if (float.IsNaN(f) || f <= 0f) 
            return 0;
        
        if (f >= 1f) 
            return 255;
        
        return (byte)(f * 255f + 0.5f);
    }
}