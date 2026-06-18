namespace Lyra.Imaging.Decoding.Support;

/// <summary>
/// Holds decoded RGBA float pixels (length <c>Width * Height * 4</c>) backed by either a managed
/// array or a block of native memory, exposing them uniformly as a <see cref="Span{T}"/>. Native
/// memory is released on <see cref="Dispose"/>; managed buffers need no cleanup.
///
/// This lets <see cref="FloatRgbaDecoderBase"/> drive both pure-managed decoders (which produce a
/// <c>float[]</c>) and native decoders (which hand back a pointer) through one code path.
/// </summary>
internal readonly struct FloatImageBuffer : IDisposable
{
    private readonly float[]? _managed;
    private readonly IntPtr _native;
    private readonly Action<IntPtr>? _free;

    public int Width { get; }
    public int Height { get; }

    private FloatImageBuffer(float[]? managed, IntPtr native, Action<IntPtr>? free, int width, int height)
    {
        _managed = managed;
        _native = native;
        _free = free;
        Width = width;
        Height = height;
    }

    /// <summary>Wraps a managed float array; no native cleanup is performed on dispose.</summary>
    public static FloatImageBuffer FromManaged(float[] pixels, int width, int height)
        => new(pixels, IntPtr.Zero, null, width, height);

    /// <summary>Wraps native memory; <paramref name="free"/> is invoked on dispose.</summary>
    public static FloatImageBuffer FromNative(IntPtr pixels, int width, int height, Action<IntPtr> free)
        => new(null, pixels, free, width, height);

    public unsafe Span<float> AsSpan()
    {
        var count = checked(Width * Height * 4);
        return _managed != null
            ? _managed.AsSpan(0, count)
            : new Span<float>((void*)_native, count);
    }

    public void Dispose() => _free?.Invoke(_native);
}