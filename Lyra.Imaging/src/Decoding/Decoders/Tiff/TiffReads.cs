using System.Runtime.InteropServices;
using Lyra.Common;
using Lyra.Imaging.Content;
using Lyra.Imaging.Decoding.Support;
using Lyra.Imaging.Interop;

namespace Lyra.Imaging.Decoding.Decoders.Tiff;

internal readonly record struct TiffRead(bool Ok, IntPtr Pixels, bool TimedOut, string Error)
{
    public int Width { get; init; }
    public int Height { get; init; }
    public uint Stride { get; init; }

    /// <summary>Owned together with the pixels.</summary>
    public IntPtr Icc { get; init; }
    public int IccSize { get; init; }

    public bool HasPixels => Ok && Pixels != IntPtr.Zero;

    public string Reason => TimedOut ? TiffReads.TimedOutReason : Error;

    public static TiffRead Failed(string error) => new(false, IntPtr.Zero, false, error);
}

internal static class TiffReads
{
    public const string TimedOutReason = "The read did not return in time; the file may be on a source that has stopped responding.";

    private static readonly TimeSpan InFlightPollInterval = TimeSpan.FromMilliseconds(250);

    public static string NativeError() => NativeErrors.GetUtf8ZOrAnsiZ(TiffNative.get_last_tiff_error());

    public static TiffRead Region(string path, int directory, bool colour, uint x, uint y, uint width, uint height, string context, CancellationToken ct, IoTally? tally = null)
    {
        uint stride = 0;

        var read = Bounded(context, (long)width * height * (colour ? 4 : 1),
            (out IntPtr buffer) => TiffNative.LoadRegion(path, directory, colour, x, y, width, height, out buffer, out stride),
            tally, ct);

        return read.HasPixels ? read with { Stride = stride } : read;
    }

    public static TiffRead RegionFromMemory(NativeFileBuffer data, int directory, bool colour, uint x, uint y, uint width, uint height)
    {
        if (TiffNative.LoadRegionFromMemory(data.Data, data.Length, directory, colour, x, y, width, height, out var pixels, out var stride)
            && pixels != IntPtr.Zero)
        {
            return new TiffRead(true, pixels, false, string.Empty) { Stride = stride };
        }

        return TiffRead.Failed(NativeError());
    }

    public static TiffRead NativeLayout(string path, int directory, TiffNative.OutputKind kind, long expectedBytes, CancellationToken ct, IoTally? tally = null)
    {
        int width = 0, height = 0;
        uint stride = 0;

        var read = Bounded($"Native layout of {Path.GetFileName(path)}", expectedBytes,
            (out IntPtr buffer) => TiffNative.LoadNative(path, directory, kind, out buffer, out width, out height, out stride),
            tally, ct);

        return read.HasPixels ? read with { Width = width, Height = height, Stride = stride } : read;
    }

    public static TiffRead WholeImage(string path, long expectedBytes, CancellationToken ct, IoTally? tally = null) =>
        Rgba($"Whole image of {Path.GetFileName(path)}", expectedBytes, tally, ct,
            (out IntPtr buffer, out int width, out int height, out IntPtr icc, out int iccSize) =>
                TiffNative.load_tiff_rgba(path, out buffer, out width, out height, out icc, out iccSize));

    public static TiffRead Directory(string path, int directory, long expectedBytes, CancellationToken ct, IoTally? tally = null) =>
        Rgba($"Page {directory} of {Path.GetFileName(path)}", expectedBytes, tally, ct,
            (out IntPtr buffer, out int width, out int height, out IntPtr icc, out int iccSize) =>
                TiffNative.LoadDirectory(path, IntPtr.Zero, 0, directory, out buffer, out width, out height, out icc, out iccSize));

    public static TiffRead LocalWholeImage(string localPath)
    {
        var ok = TiffNative.load_tiff_rgba(localPath, out var pixels, out var width, out var height, out var icc, out var iccSize);
        return Rgba(ok, pixels, width, height, icc, iccSize);
    }

    public static TiffRead FromMemory(IntPtr data, ulong length)
    {
        var ok = TiffNative.LoadFromMemory(data, length, out var pixels, out var width, out var height, out var icc, out var iccSize);
        return Rgba(ok, pixels, width, height, icc, iccSize);
    }

    public static long SafeFileLength(string path)
    {
        try
        {
            return new FileInfo(path).Length;
        }
        catch (Exception ex)
        {
            Logger.Debug($"[TiffDecoder] Could not size {path} for its read bound: {ex.Message}");
            return 0;
        }
    }

    private static TiffRead Rgba(bool ok, IntPtr pixels, int width, int height, IntPtr icc, int iccSize)
    {
        if (ok && pixels != IntPtr.Zero)
            return new TiffRead(true, pixels, false, string.Empty) { Width = width, Height = height, Icc = icc, IccSize = iccSize };

        var error = NativeError();

        if (icc != IntPtr.Zero)
            TiffNative.free_tiff_pixels(icc);

        return TiffRead.Failed(error);
    }

    private delegate bool RgbaCall(out IntPtr pixels, out int width, out int height, out IntPtr icc, out int iccSize);

    private static TiffRead Rgba(string context, long expectedBytes, IoTally? tally, CancellationToken ct, RgbaCall call)
    {
        int width = 0, height = 0, iccSize = 0;
        var icc = IntPtr.Zero;

        var read = Bounded(context, expectedBytes,
            (out IntPtr buffer) => call(out buffer, out width, out height, out icc, out iccSize),
            tally, ct,
            releaseLate: late =>
            {
                TiffNative.free_tiff_pixels(late);

                if (icc != IntPtr.Zero)
                    TiffNative.free_tiff_pixels(icc);
            });

        return read.HasPixels ? read with { Width = width, Height = height, Icc = icc, IccSize = iccSize } : read;
    }

    private static TiffRead Bounded(string context, long expectedBytes, BoundedNativeRead.ReadWithBuffer call, IoTally? tally, CancellationToken ct, Action<IntPtr>? releaseLate = null)
    {
        string? failure = null;
        var watch = new ReadWatch(tally);

        bool ok;
        IntPtr pixels;
        bool timedOut;

        try
        {
            ok = BoundedNativeRead.Attempt(context, expectedBytes,
                (out IntPtr buffer) =>
                {
                    var counter = watch.Begin();
                    var watched = counter != IntPtr.Zero && TiffNative.SetIoProgress(counter);

                    try
                    {
                        var read = call(out buffer);

                        // The native error is per-thread.
                        if (!read)
                            failure = NativeError();

                        return read;
                    }
                    finally
                    {
                        if (watched)
                            TiffNative.SetIoProgress(IntPtr.Zero);

                        watch.Complete();
                    }
                },
                out pixels, out timedOut, releaseLate ?? TiffNative.free_tiff_pixels, ct: ct);
        }
        catch
        {
            watch.Abandon();
            throw;
        }

        if (timedOut)
            watch.Abandon();

        return new TiffRead(ok, pixels, timedOut, failure ?? string.Empty);
    }

    /// <summary>
    /// Reports one native read into its tally while it is in flight and when it returns; an
    /// abandoned read reports nothing.
    /// </summary>
    private sealed class ReadWatch(IoTally? tally)
    {
        private readonly Lock _gate = new();

        /// <summary>Written by the native read; freed only by the reading thread, which may outlive an abandoning caller.</summary>
        private IntPtr _counter;

        private Timer? _poll;
        private bool _abandoned;

        public IntPtr Begin()
        {
            lock (_gate)
            {
                if (_abandoned || tally is not { IsLive: true })
                    return IntPtr.Zero;

                _counter = Marshal.AllocHGlobal(sizeof(long));
                Marshal.WriteInt64(_counter, 0);

                _poll = new Timer(_ => Report(), null, InFlightPollInterval, InFlightPollInterval);
                return _counter;
            }
        }

        private void Report()
        {
            lock (_gate)
            {
                if (_abandoned || _counter == IntPtr.Zero)
                    return;

                unsafe
                {
                    tally!.ReportInFlight(Interlocked.Read(ref *(long*)_counter));
                }
            }
        }

        public void Complete()
        {
            lock (_gate)
            {
                if (!_abandoned)
                    tally?.AddLastRead();

                StopPolling();

                if (_counter != IntPtr.Zero)
                {
                    Marshal.FreeHGlobal(_counter);
                    _counter = IntPtr.Zero;
                }
            }
        }

        public void Abandon()
        {
            lock (_gate)
            {
                _abandoned = true;
                StopPolling();
            }
        }

        private void StopPolling()
        {
            _poll?.Dispose();
            _poll = null;
        }
    }
}

/// <summary>Sums the native reads of one load into a single transfer.</summary>
/// <param name="live">Receives the running total as reads progress.</param>
internal sealed class IoTally(Composite? live = null)
{
    private long _bytes;
    private long _microseconds;

    public bool IsLive => live is not null;

    public void Add(long bytes, double ms)
    {
        var total = Interlocked.Add(ref _bytes, bytes);
        Interlocked.Add(ref _microseconds, (long)(ms * 1000));

        live?.ReportTransferred(total);
    }

    public void ReportInFlight(long inFlightBytes) => live?.ReportTransferred(Interlocked.Read(ref _bytes) + inFlightBytes);

    /// <summary>Adds the calling thread's last native read.</summary>
    public void AddLastRead()
    {
        if (TiffNative.LastIo() is { } io)
            Add(io.Bytes, io.Ms);
    }

    public void ReportTo(Composite? composite)
    {
        var bytes = Interlocked.Read(ref _bytes);
        if (composite is null || bytes <= 0)
            return;

        composite.CompleteTransfer(bytes, Interlocked.Read(ref _microseconds) / 1000.0);
    }
}
