using System.Buffers;

namespace Lyra.Imaging.Psd.Core.Decode.Pixel;

/// <summary>
/// 4-bytes-per-pixel RGBA-family surface (row-major, optional padding via Stride).
/// Pixel layout is defined by the writer, not by the surface.
/// </summary>
public sealed class RgbaSurface : IDisposable
{
    public int Width { get; }
    public int Height { get; }
    public int Stride { get; } // bytes per row (>= Width * 4)

    public IMemoryOwner<byte> Owner { get; }
    public Memory<byte> Memory { get; }
    public Span<byte> Span => Memory.Span;

    public RgbaSurface(
        int width,
        int height,
        IMemoryOwner<byte> owner,
        int stride)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        ArgumentOutOfRangeException.ThrowIfLessThan(stride, width * 4);

        Width = width;
        Height = height;
        Stride = stride;
        Owner = owner ?? throw new ArgumentNullException(nameof(owner));

        var requiredBytes = checked(stride * height);
        if (owner.Memory.Length < requiredBytes)
            throw new ArgumentException($"Owner buffer too small: {owner.Memory.Length} < {requiredBytes}", nameof(owner));

        Memory = owner.Memory[..requiredBytes];
    }

    /// <summary>
    /// Returns a span covering the pixel data for a single row (Width * 4 bytes).
    /// Padding bytes (if any) are excluded.
    /// </summary>
    public Span<byte> GetRowSpan(int y)
    {
        return (uint)y < (uint)Height 
            ? Span.Slice(y * Stride, Width * 4) 
            : throw new ArgumentOutOfRangeException(nameof(y));
    }

    public void Dispose() => Owner.Dispose();
}