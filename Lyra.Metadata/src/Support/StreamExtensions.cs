namespace Lyra.Metadata.Support;

internal static class StreamExtensions
{
    public static bool ReadAt(this Stream stream, long offset, Span<byte> buffer)
    {
        if (offset < 0 || offset > stream.Length - buffer.Length)
            return false;

        stream.Position = offset;
        return stream.ReadAtLeast(buffer, buffer.Length, throwOnEndOfStream: false) == buffer.Length;
    }

    public static byte[]? ReadBytesAt(this Stream stream, long offset, long length, long maxBytes = long.MaxValue)
    {
        if (length < 0 || length > Math.Min(maxBytes, Array.MaxLength) || offset < 0 || offset > stream.Length - length)
            return null;

        var bytes = new byte[length];
        return stream.ReadAt(offset, bytes) ? bytes : null;
    }

    public static PositionScope KeepPosition(this Stream stream) => new(stream);

    public readonly struct PositionScope(Stream stream) : IDisposable
    {
        private readonly long _position = stream.Position;

        public void Dispose() => stream.Position = _position;
    }
}