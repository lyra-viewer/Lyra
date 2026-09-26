using System.Diagnostics;

namespace Lyra.Imaging.Content;

public sealed class LoadMeasurement
{
    private Stopwatch? _stopwatch;

    private long _transferBytesDone;
    private long _transferBytesLive;
    private long _transferMicroseconds;
    private int _transferReads;
    private long _pausedMicroseconds;
    
    public double DecodeEstimateMs { get; internal set; }

    public long TransferBytesTotal { get; internal set; }

    public double? ReadyMs { get; private set; }

    public double? CompleteMs { get; private set; }

    public double ElapsedMs => _stopwatch?.Elapsed.TotalMilliseconds ?? 0;

    public long TransferBytesRead => Volatile.Read(ref _transferBytesDone) + Volatile.Read(ref _transferBytesLive);
    
    public bool TransferMeasured => Volatile.Read(ref _transferReads) > 0;
    
    public double? TransferMs => TransferMeasured
        ? Volatile.Read(ref _transferMicroseconds) / 1000.0
        : null;
    
    public double PausedMs => Volatile.Read(ref _pausedMicroseconds) / 1000.0;

    public double? DecodeMs => CompleteMs is { } total && TransferMs is { } transfer
        ? Math.Max(0, total - transfer - PausedMs)
        : null;

    internal void Begin()
    {
        _stopwatch = Stopwatch.StartNew();

        ReadyMs = null;
        CompleteMs = null;

        Volatile.Write(ref _transferBytesDone, 0);
        Volatile.Write(ref _transferBytesLive, 0);
        Volatile.Write(ref _transferMicroseconds, 0);
        Volatile.Write(ref _transferReads, 0);
        Volatile.Write(ref _pausedMicroseconds, 0);
    }

    internal void AddPause(double ms)
    {
        if (Measuring && ms > 0)
            Interlocked.Add(ref _pausedMicroseconds, (long)(ms * 1000));
    }
    
    private bool Measuring => _stopwatch is { IsRunning: true };

    internal void ReportTransferred(long bytesSoFar)
    {
        if (Measuring)
            Volatile.Write(ref _transferBytesLive, bytesSoFar);
    }

    internal void CompleteTransfer(long bytes, double ms)
    {
        if (!Measuring)
            return;

        Interlocked.Add(ref _transferBytesDone, bytes);
        Volatile.Write(ref _transferBytesLive, 0);
        Interlocked.Add(ref _transferMicroseconds, (long)(ms * 1000));
        Interlocked.Increment(ref _transferReads);
    }

    internal void MarkReady()
    {
        if (_stopwatch is { IsRunning: true })
            ReadyMs = _stopwatch.Elapsed.TotalMilliseconds;
    }

    internal void MarkComplete()
    {
        if (_stopwatch is not { IsRunning: true })
            return;

        _stopwatch.Stop();
        CompleteMs = _stopwatch.Elapsed.TotalMilliseconds;
    }
}