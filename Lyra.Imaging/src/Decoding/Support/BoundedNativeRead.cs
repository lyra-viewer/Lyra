using Lyra.Common;

namespace Lyra.Imaging.Decoding.Support;

/// <summary>
/// Runs one blocking native region/band read with a hard time bound. libtiff's region reads are a
/// single blocking call with no cancellation hook - fine against local disk, but a stalled network
/// share (a wedged NFS mount, a dropped SMB connection) can leave one hanging indefinitely with
/// nothing to interrupt it. A <see cref="CancellationToken"/> only gets checked between reads, never
/// during one, so a single stuck call defeats cancellation entirely and parks that decode thread
/// for the life of the process.
/// </summary>
internal static class BoundedNativeRead
{
    /// <summary>
    /// The slowest rate a transfer that is actually working is assumed to manage, set far below
    /// any real share so the bound comes out many times what a legitimate read of that size takes.
    /// </summary>
    private const long FloorBytesPerSecond = 512 * 1024;

    /// <summary>Applied to small reads, where the rate above would allow implausibly little time.</summary>
    private static readonly TimeSpan MinimumBound = TimeSpan.FromSeconds(60);

    /// <summary>The point past which waiting longer serves nobody, however large the read.</summary>
    private static readonly TimeSpan MaximumBound = TimeSpan.FromMinutes(10);

    /// <summary>How long a read of this size is given before it counts as stuck rather than slow.</summary>
    internal static TimeSpan BoundFor(long expectedBytes)
    {
        if (expectedBytes <= 0)
            return MinimumBound;
        
        var seconds = (double)expectedBytes / FloorBytesPerSecond;
        if (seconds <= MinimumBound.TotalSeconds)
            return MinimumBound;

        return seconds >= MaximumBound.TotalSeconds ? MaximumBound : TimeSpan.FromSeconds(seconds);
    }

    /// <summary>Performs the read, handing back whatever buffer it allocated.</summary>
    internal delegate bool ReadWithBuffer(out IntPtr pixels);

    /// <summary>
    /// Runs <paramref name="read"/> to completion or to its bound, whichever comes first.
    /// A timeout is reported as a failed read, which every caller already handles the way it would
    /// a real native decode failure; it is never rethrown.
    /// </summary>
    public static bool Run(string context, long expectedBytes, ReadWithBuffer read, out IntPtr pixels, out bool timedOut, Action<IntPtr> releaseLate, TimeSpan? timeout = null)
    {
        var bound = timeout ?? BoundFor(expectedBytes);

        var task = Task.Run(() =>
        {
            var ok = read(out var buffer);
            return (Ok: ok, Pixels: buffer);
        });

        if (Task.WaitAny([task], bound) == 0)
        {
            var (ok, buffer) = task.GetAwaiter().GetResult();

            timedOut = false;
            pixels = ok ? buffer : IntPtr.Zero;
            return ok;
        }

        timedOut = true;
        pixels = IntPtr.Zero;

        Logger.Warning($"[BoundedNativeRead] {context} did not return within {bound.TotalSeconds:F0}s; abandoning it rather than waiting further.");
        
        task.ContinueWith(finished =>
        {
            if (finished is { IsCompletedSuccessfully: true, Result.Ok: true, Result.Pixels: var late } && late != IntPtr.Zero)
            {
                releaseLate(late);
                Logger.Debug($"[BoundedNativeRead] {context} returned after it was abandoned; its buffer was freed.");
            }
        }, TaskScheduler.Default);

        return false;
    }
}
