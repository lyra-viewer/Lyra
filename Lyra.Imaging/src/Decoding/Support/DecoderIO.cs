using System.Diagnostics;
using Lyra.Common;

namespace Lyra.Imaging.Decoding.Support;

public static class DecoderIO
{
    private const int SequentialBuffer = 64 * 1024;
    private const int RandomBuffer = 16 * 1024;
    private const int UnsizedInitialBuffer = 64 * 1024;
    
    internal const int ReadChunk = 1024 * 1024;

    public static FileStream OpenSequentialRead(string path)
        => new(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            SequentialBuffer,
            FileOptions.SequentialScan
        );

    public static FileStream OpenRandomAccessRead(string path)
        => new(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            RandomBuffer,
            FileOptions.RandomAccess
        );

    /// <summary>
    /// Reads a whole file into memory, checking <paramref name="ct"/> between chunks and reporting
    /// the running byte total to <paramref name="onProgress"/>.
    /// </summary>
    /// <param name="elapsedMs">
    /// How long the read took. Handed back rather than recorded anywhere: what a caller does with
    /// a duration is its business, and this stays an IO helper that knows nothing about images.
    /// Not set when the read throws - a failed read's timing has no consumer.
    /// </param>
    /// <exception cref="OperationCanceledException">Cancellation was requested mid-read.</exception>
    /// <exception cref="IOException">The file does not fit in a single array.</exception>
    public static byte[] ReadAllBytes(string path, CancellationToken ct, out double elapsedMs, Action<long>? onProgress = null)
    {
        var start = Stopwatch.GetTimestamp();
        using var stream = OpenSequentialRead(path);

        var length = SafeLength(stream, path);
        if (length > Array.MaxLength)
            throw new IOException($"[DecoderIO] File is too large to read into memory ({length} bytes): {path}");

        var buffer = new byte[length > 0 ? length : UnsizedInitialBuffer];
        var total = 0;

        while (true)
        {
            ct.ThrowIfCancellationRequested();

            if (total == buffer.Length)
            {
                if (buffer.Length >= Array.MaxLength)
                    throw new IOException($"[DecoderIO] File is too large to read into memory: {path}");

                Array.Resize(ref buffer, (int)Math.Min((long)buffer.Length * 2, Array.MaxLength));
            }

            var read = stream.Read(buffer.AsSpan(total, Math.Min(ReadChunk, buffer.Length - total)));
            if (read == 0)
                break;

            total += read;
            onProgress?.Invoke(total);
        }

        elapsedMs = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        return total == buffer.Length ? buffer : buffer[..total];
    }

    private static long SafeLength(FileStream stream, string path)
    {
        try
        {
            return stream.Length;
        }
        catch (Exception ex)
        {
            Logger.Debug($"[DecoderIO] Length unavailable, reading until EOF: {path} ({ex.Message})");
            return 0;
        }
    }
}