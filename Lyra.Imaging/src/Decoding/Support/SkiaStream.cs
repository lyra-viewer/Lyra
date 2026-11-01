using SkiaSharp;

namespace Lyra.Imaging.Decoding.Support;

/// <summary>
/// Hands a .NET stream to Skia's codecs without ever throwing back into them.
/// </summary>
/// <remarks>
/// Skia calls the stream from native code, where an exception cannot unwind, so one escaping a
/// callback terminates the process. SkiaSharp's own adapter lets that happen: a BMP whose pixel
/// offset points gigabytes past its end makes Skia ask to skip that far, and the adapter's skip
/// throws. Here every callback answers within the stream, and a read that fails reads nothing,
/// which Skia already treats as a truncated file.
/// </remarks>
internal sealed class SkiaStream : SKAbstractManagedStream
{
    private const int MaxChunk = 1 << 20;

    private readonly Stream _stream;

    /// <param name="stream">Read by Skia; left open, for the caller to dispose after the codec.</param>
    public SkiaStream(Stream stream) : base(owns: true)
    {
        _stream = stream;
    }

    /// <summary>Opens a codec over <paramref name="stream"/>. The caller disposes the codec before the stream.</summary>
    public static SKCodec? Codec(Stream stream, out SKCodecResult result) => SKCodec.Create(new SkiaStream(stream), out result);

    /// <inheritdoc cref="Codec(Stream, out SKCodecResult)"/>
    public static SKCodec? Codec(Stream stream) => Codec(stream, out _);

    protected override unsafe IntPtr OnRead(IntPtr buffer, IntPtr size)
    {
        try
        {
            var wanted = (long)size;
            if (wanted <= 0)
                return IntPtr.Zero;

            if (buffer == IntPtr.Zero)
                return (IntPtr)Skip(wanted);

            var total = 0L;
            while (total < wanted)
            {
                var chunk = (int)Math.Min(wanted - total, MaxChunk);
                var read = _stream.Read(new Span<byte>((byte*)buffer + total, chunk));
                if (read <= 0)
                    break;

                total += read;
            }

            return (IntPtr)total;
        }
        catch (Exception)
        {
            return IntPtr.Zero;
        }
    }

    private long Skip(long count)
    {
        if (_stream.CanSeek)
        {
            var step = Math.Clamp(_stream.Length - _stream.Position, 0, count);
            _stream.Position += step;
            return step;
        }

        Span<byte> discard = stackalloc byte[4096];
        var skipped = 0L;

        while (skipped < count)
        {
            var read = _stream.Read(discard[..(int)Math.Min(discard.Length, count - skipped)]);
            if (read <= 0)
                break;

            skipped += read;
        }

        return skipped;
    }

    protected override IntPtr OnPeek(IntPtr buffer, IntPtr size)
    {
        try
        {
            if (!_stream.CanSeek)
                return IntPtr.Zero;

            var position = _stream.Position;
            var read = OnRead(buffer, size);
            _stream.Position = position;

            return read;
        }
        catch (Exception)
        {
            return IntPtr.Zero;
        }
    }

    protected override bool OnIsAtEnd()
    {
        try
        {
            return !_stream.CanSeek || _stream.Position >= _stream.Length;
        }
        catch (Exception)
        {
            return true;
        }
    }

    protected override bool OnHasPosition() => Safely(() => _stream.CanSeek);

    protected override bool OnHasLength() => Safely(() => _stream.CanSeek);

    protected override bool OnRewind() => MoveTo(0);

    protected override IntPtr OnGetPosition()
    {
        try
        {
            return (IntPtr)_stream.Position;
        }
        catch (Exception)
        {
            return IntPtr.Zero;
        }
    }

    protected override IntPtr OnGetLength()
    {
        try
        {
            return (IntPtr)_stream.Length;
        }
        catch (Exception)
        {
            return IntPtr.Zero;
        }
    }

    protected override bool OnSeek(IntPtr position) => MoveTo((long)position);

    protected override bool OnMove(int offset)
    {
        try
        {
            return _stream.CanSeek && MoveTo(_stream.Position + offset);
        }
        catch (Exception)
        {
            return false;
        }
    }

    private bool MoveTo(long position)
    {
        try
        {
            if (!_stream.CanSeek)
                return false;

            _stream.Position = Math.Clamp(position, 0, _stream.Length);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    protected override IntPtr OnCreateNew() => IntPtr.Zero;

    private static bool Safely(Func<bool> question)
    {
        try
        {
            return question();
        }
        catch (Exception)
        {
            return false;
        }
    }
}