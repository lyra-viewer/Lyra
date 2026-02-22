namespace Lyra.Imaging.Psd.Core.Common;

public enum PsdDepth : short
{
    Bit8 = 8,
    Bit16 = 16,
    Bit32 = 32
}

public static class PsdDepthExtensions
{
    public static int BytesPerChannel(this PsdDepth depth)
        => ((int)depth) >> 3; // divide by 8

    public static int RowBytes(this PsdDepth depth, int width)
        => checked(width * depth.BytesPerChannel());
}

public static class PsdDepthUtil
{
    public static PsdDepth FromBitsPerChannel(int bitsPerChannel) => bitsPerChannel switch
    {
        8 => PsdDepth.Bit8,
        16 => PsdDepth.Bit16,
        32 => PsdDepth.Bit32,
        _ => throw new NotSupportedException($"BitsPerChannel {bitsPerChannel} not supported (expected 8/16/32).")
    };
}