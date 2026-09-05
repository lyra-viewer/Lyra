using System.Diagnostics;
using System.Runtime.InteropServices;
using Lyra.Common;

namespace Lyra.Imaging.Decoding.Support;

/// <summary>
/// A whole file read into unmanaged memory, for handing to a native decoder that takes a buffer.
/// </summary>
internal sealed class NativeFileBuffer : IDisposable
{
    /// <summary>Start of the buffer. Valid until <see cref="Dispose"/>.</summary>
    public IntPtr Data { get; private set; }

    /// <summary>Bytes actually read, which is the file's length unless it shrank mid-read.</summary>
    public ulong Length { get; private set; }

    private NativeFileBuffer(IntPtr data, ulong length)
    {
        Data = data;
        Length = length;
    }

    /// <summary>Reads a whole file, measuring it exactly as <see cref="DecoderIO.ReadAllBytes"/> does.</summary>
    /// <param name="elapsedMs">How long the read took. Not set when the read throws.</param>
    /// <param name="onProgress">Called with the running byte total as the read progresses.</param>
    /// <exception cref="OperationCanceledException">Cancellation was requested mid-read.</exception>
    public static unsafe NativeFileBuffer Read(string path, CancellationToken ct, out double elapsedMs, Action<long>? onProgress = null)
    {
        using var stream = DecoderIO.OpenSequentialRead(path);

        var length = stream.Length;
        if (length <= 0)
            throw new IOException($"[NativeFileBuffer] Refusing to read an empty or unsized file: {path}");

        var data = Marshal.AllocHGlobal((nint)length);
        var start = Stopwatch.GetTimestamp();
        long total = 0;

        try
        {
            while (total < length)
            {
                ct.ThrowIfCancellationRequested();

                var want = (int)Math.Min(DecoderIO.ReadChunk, length - total);
                var read = stream.Read(new Span<byte>((byte*)data + total, want));
                if (read == 0)
                    break;

                total += read;
                onProgress?.Invoke(total);
            }
        }
        catch
        {
            Marshal.FreeHGlobal(data);
            throw;
        }

        elapsedMs = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        return new NativeFileBuffer(data, (ulong)total);
    }
    
    public unsafe Stream AsStream() => new UnmanagedMemoryStream((byte*)Data, (long)Length);

    public void Dispose()
    {
        if (Data == IntPtr.Zero)
            return;

        Marshal.FreeHGlobal(Data);
        Data = IntPtr.Zero;
        Length = 0;
    }
    
    public static bool ShouldBuffer(long sizeInBytes)
    {
        if (sizeInBytes <= 0)
            return false;

        var available = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes;
        var ceiling = available > 0 ? available / 4 : 512L * 1024 * 1024;

        if (sizeInBytes <= ceiling)
            return true;

        Logger.Info($"[NativeFileBuffer] {sizeInBytes / (1024 * 1024)} MB is over the {ceiling / (1024 * 1024)} MB buffering ceiling; decoding from the path instead, without measuring the read.");
        return false;
    }
}
