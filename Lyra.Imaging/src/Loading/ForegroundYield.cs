using System.Diagnostics;
using Lyra.Common;

namespace Lyra.Imaging.Loading;

/// <summary>
/// Lets a load that is not on screen step aside while the one that is on screen is loading: a
/// preload, and equally a load the user has since navigated away from. Thread priority cannot do
/// this where it matters most: it orders CPU time, and a network share hands its bandwidth to
/// whichever read is queued, whatever the priority of the thread that issued it. So such a load
/// instead waits between reads - before it starts, and before each read it makes - until the
/// foreground load is through, and picks up where it stopped.
/// </summary>
internal static class ForegroundYield
{
    /// <summary>How often a waiting load looks again. Short against any read worth waiting for.</summary>
    internal static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(50);

    private sealed record Scope(string Name, Func<bool> ForegroundBusy, Action<double> Paused);

    private static readonly AsyncLocal<Scope?> Current = new();

    /// <summary>
    /// Marks everything this flow does until the result is disposed as the load of
    /// <paramref name="name"/>, which steps aside whenever <paramref name="foregroundBusy"/> says so.
    /// </summary>
    internal static IDisposable EnterLoad(string name, Func<bool> foregroundBusy, Action<double> paused)
    {
        var previous = Current.Value;
        Current.Value = new Scope(name, foregroundBusy, paused);

        return new Restore(previous);
    }
    
    internal static void WaitForForeground(CancellationToken ct)
    {
        if (Current.Value is not { } scope || !scope.ForegroundBusy())
            return;

        var waited = Stopwatch.StartNew();

        while (scope.ForegroundBusy())
        {
            if (ct.WaitHandle.WaitOne(PollInterval))
                ct.ThrowIfCancellationRequested();
        }

        waited.Stop();
        scope.Paused(waited.Elapsed.TotalMilliseconds);

        Logger.Debug($"[ForegroundYield] Load of {scope.Name} waited {waited.Elapsed.TotalMilliseconds:F0} ms for the image on screen.");
    }

    private sealed class Restore(Scope? previous) : IDisposable
    {
        public void Dispose() => Current.Value = previous;
    }
}
