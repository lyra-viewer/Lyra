using System.Numerics;

namespace Lyra.FileLoader.Duplicates.Perceptual;

/// <summary>
/// Difference hash (dHash) over a small grayscale buffer: compares each pixel to its
/// right neighbor, yielding one bit per comparison.
/// </summary>
public static class PerceptualHash
{
    // 9×8 grid → 8 horizontal comparisons per row × 8 rows = 64 bits.
    public const int Width = 9;
    public const int Height = 8;
    public const int SampleCount = Width * Height;

    // A "computed" marker in the top bit guarantees a real hash is never 0, so it can
    // never be mistaken for the "not computed" sentinel (FileRecord.HasPHash => PHash != 0).
    // A flat/solid image legitimately produces an all-zero dHash, so this matters here
    // (unlike the exact ContentHash, where 0 is astronomically unlikely). The marker is
    // identical across all computed hashes, so it contributes 0 to every Hamming distance.
    private const ulong ComputedMarker = 1UL << 63;

    public static ulong Compute(ReadOnlySpan<byte> luma)
    {
        if (luma.Length != SampleCount)
            throw new ArgumentException($"Expected {SampleCount} samples, got {luma.Length}.", nameof(luma));

        ulong hash = 0;
        var bit = 0;

        for (var y = 0; y < Height; y++)
        {
            var row = y * Width;
            for (var x = 0; x < Width - 1; x++)
            {
                if (luma[row + x] < luma[row + x + 1])
                    hash |= 1UL << bit;
                bit++;
            }
        }

        return hash | ComputedMarker;
    }

    public static int Distance(ulong a, ulong b) => BitOperations.PopCount(a ^ b);
}
