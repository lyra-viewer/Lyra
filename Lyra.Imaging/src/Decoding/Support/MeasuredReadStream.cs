using System.Diagnostics;

namespace Lyra.Imaging.Decoding.Support;

internal sealed class MeasuredReadStream : Stream
{
    private readonly Stream _inner;
    private readonly Action<long>? _onProgress;
    private readonly Action<long, double>? _onCompleted;

    private long _bytesRead;
    private long _ticks;
    private bool _disposed;

    /// <param name="onProgress">Called with the running byte total as the read progresses.</param>
    /// <param name="onCompleted">Called once on dispose with the final byte total and elapsed milliseconds.</param>
    public MeasuredReadStream(Stream inner, Action<long>? onProgress = null, Action<long, double>? onCompleted = null)
    {
        _inner = inner;
        _onProgress = onProgress;
        _onCompleted = onCompleted;
    }

    public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

    public override int Read(Span<byte> buffer)
    {
        var start = Stopwatch.GetTimestamp();
        int read;
        try
        {
            read = _inner.Read(buffer);
        }
        finally
        {
            _ticks += Stopwatch.GetTimestamp() - start;
        }

        if (read > 0)
        {
            _bytesRead += read;
            _onProgress?.Invoke(_bytesRead);
        }

        return read;
    }

    public override int ReadByte()
    {
        Span<byte> one = stackalloc byte[1];
        return Read(one) == 1 ? one[0] : -1;
    }

    protected override void Dispose(bool disposing)
    {
        // Skia hands its codec ownership of the stream while the caller still holds a using, so
        // this runs twice; the measurement must only be handed over once.
        if (!_disposed)
        {
            _disposed = true;
            _onCompleted?.Invoke(_bytesRead, Stopwatch.GetElapsedTime(0, _ticks).TotalMilliseconds);

            if (disposing)
                _inner.Dispose();
        }

        base.Dispose(disposing);
    }

    public override bool CanRead => _inner.CanRead;
    public override bool CanSeek => _inner.CanSeek;
    public override bool CanWrite => false;
    public override long Length => _inner.Length;

    public override long Position
    {
        get => _inner.Position;
        set => _inner.Position = value;
    }

    public override long Seek(long offset, SeekOrigin origin) => _inner.Seek(offset, origin);
    public override void Flush() => _inner.Flush();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}
