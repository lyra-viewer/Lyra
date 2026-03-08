using System.Buffers.Binary;
using System.Runtime.CompilerServices;

namespace Lyra.Imaging.Psd.Core.Decode.Color;

// ============================================================================
//  PERFORMANCE CRITICAL – PSD SAMPLE CONVERSION
// ----------------------------------------------------------------------------
//  This class performs per-sample bit depth conversion during PSD/PSB decode.
//  It is executed for every pixel in large images (including multi-gigabyte PSB files).
//
//  Hot Path Characteristics:
//    - Called millions to billions of times for large images.
//    - Must not allocate.
//    - Must avoid division where possible.
//    - Must avoid unnecessary Span slicing.
//    - Must avoid extra bounds checks in inner loops.
//    - Must remain branch-predictable.
//
//  Implementation Notes:
//    - U16 -> U8 conversion uses a division-free exact transform.
//    - Float conversion assumes normalized [0..1] PSD float encoding.
//    - All methods assume caller guarantees valid buffer sizes.
//    - No defensive re-checking inside inner loops.
//
//  PERFORMANCE CONTRACT:
//    Any modification here must be benchmarked against large (≥3GB) PSB files.
//    Refactors for readability must not introduce hidden allocations,
//    division, LINQ, or additional branching.
//
//  Verified against:
//    - Reference PSD/PSB corpus
//    - 3GB production PSB benchmark
// ============================================================================
internal static class PsdSampleConvert
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static byte U16To8(ushort v)
    {
        uint x = v + 128u;
        return (byte)((x - (x >> 8)) >> 8);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static ushort ReadU16BE(ReadOnlySpan<byte> src, int offset)
        => (ushort)((src[offset] << 8) | src[offset + 1]);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static uint ReadU32BE(ReadOnlySpan<byte> src, int offset)
        => BinaryPrimitives.ReadUInt32BigEndian(src.Slice(offset, 4));

    public static void Row16BeTo8(ReadOnlySpan<byte> src16be, Span<byte> dst8)
    {
        for (int i = 0, si = 0; i < dst8.Length; i++, si += 2)
        {
            var v = ReadU16BE(src16be, si);
            dst8[i] = U16To8(v);
        }
    }

    public static void Row32FloatBeTo8(ReadOnlySpan<byte> src32be, Span<byte> dst8)
    {
        for (int i = 0, si = 0; i < dst8.Length; i++, si += 4)
        {
            var bits = ReadU32BE(src32be, si);
            var f = BitConverter.Int32BitsToSingle(unchecked((int)bits));
            dst8[i] = Float01To8(f);
        }
    }

    public static byte SampleTo8(ReadOnlySpan<byte> row, int offsetBytes, int bpcBytes)
    {
        return bpcBytes switch
        {
            1 => row[offsetBytes],
            2 => U16To8(ReadU16BE(row, offsetBytes)),
            4 => Float01To8(BitConverter.Int32BitsToSingle(unchecked((int)BinaryPrimitives.ReadUInt32BigEndian(row.Slice(offsetBytes, 4))))),
            _ => throw new NotSupportedException($"Unsupported bytes-per-channel: {bpcBytes}.")
        };
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static byte Float01To8(float f)
    {
        if (float.IsNaN(f) || f <= 0f)
            return 0;

        if (f >= 1f)
            return 255;

        return (byte)(f * 255f + 0.5f);
    }
}