using Lyra.Common;
using Lyra.Imaging.Loading;

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
    /// Runs <paramref name="read"/> to completion, to its bound, or to <paramref name="ct"/> being
    /// canceled, whichever comes first.
    /// </summary>
    public static bool Attempt(string context, long expectedBytes, ReadWithBuffer read, out IntPtr pixels, out bool timedOut, Action<IntPtr> releaseLate, TimeSpan? timeout = null, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        // Preload work steps aside here, between reads, while the image on screen loads.
        ForegroundYield.WaitForForeground(ct);

        var bound = timeout ?? BoundFor(expectedBytes);

        var task = LaunchAtCallerPriority(read);

        int finished;
        try
        {
            finished = Task.WaitAny([task], (int)Math.Min(int.MaxValue, bound.TotalMilliseconds), ct);
        }
        catch (OperationCanceledException)
        {
            Logger.Debug($"[BoundedNativeRead] {context} was cancelled mid-read; abandoning it.");
            ReleaseWhenDone(task, context, releaseLate);
            throw;
        }

        if (finished == 0)
        {
            var (ok, buffer) = task.GetAwaiter().GetResult();

            timedOut = false;
            pixels = ok ? buffer : IntPtr.Zero;
            return ok;
        }

        timedOut = true;
        pixels = IntPtr.Zero;

        Logger.Warning($"[BoundedNativeRead] {context} did not return within {bound.TotalSeconds:F0}s; abandoning it rather than waiting further.");

        ReleaseWhenDone(task, context, releaseLate);

        return false;
    }

    /// <summary>
    /// Starts the read off the calling thread, at the calling thread's priority. A pool thread
    /// would do at Normal, but a preload worker runs below that so the image on screen gets the
    /// CPU first - and handing its reads to the pool would quietly undo that for all the work
    /// that matters. Pool threads are shared, so their priority is left alone and the read gets
    /// a thread of its own instead.
    /// </summary>
    private static Task<(bool Ok, IntPtr Pixels)> LaunchAtCallerPriority(ReadWithBuffer read)
    {
        var priority = Thread.CurrentThread.Priority;

        if (priority == ThreadPriority.Normal)
            return Task.Run(() => Invoke(read));

        var done = new TaskCompletionSource<(bool Ok, IntPtr Pixels)>(TaskCreationOptions.RunContinuationsAsynchronously);

        var thread = new Thread(() =>
        {
            try
            {
                done.SetResult(Invoke(read));
            }
            catch (Exception ex)
            {
                done.SetException(ex);
            }
        })
        {
            IsBackground = true,
            Priority = priority,
            Name = $"BoundedRead-{Thread.CurrentThread.Name ?? priority.ToString()}"
        };

        thread.Start();
        return done.Task;
    }

    private static (bool Ok, IntPtr Pixels) Invoke(ReadWithBuffer read)
    {
        var ok = read(out var buffer);
        return (ok, buffer);
    }

    /// <summary>Frees whatever an abandoned read hands back, whenever it gets round to it.</summary>
    private static void ReleaseWhenDone(Task<(bool Ok, IntPtr Pixels)> task, string context, Action<IntPtr> releaseLate) =>
        task.ContinueWith(finished =>
        {
            if (finished is { IsCompletedSuccessfully: true, Result.Ok: true, Result.Pixels: var late } && late != IntPtr.Zero)
            {
                releaseLate(late);
                Logger.Debug($"[BoundedNativeRead] {context} returned after it was abandoned; its buffer was freed.");
            }
        }, TaskScheduler.Default);
}
