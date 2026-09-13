using System.Diagnostics;

namespace Lyra.Imaging.Content;

public sealed class LoadMeasurement
{
    private Stopwatch? _stopwatch;

    private long _transferBytesDone;
    private long _transferBytesLive;
    private long _transferMicroseconds;
    private int _transferReads;
    
    public double DecodeEstimateMs { get; internal set; }
    
    public bool EstimateIncludesTransfer { get; internal set; }

    public long TransferBytesTotal { get; internal set; }

    public double? ReadyMs { get; private set; }

    public double? CompleteMs { get; private set; }

    public double ElapsedMs => _stopwatch?.Elapsed.TotalMilliseconds ?? 0;

    public long TransferBytesRead => Volatile.Read(ref _transferBytesDone) + Volatile.Read(ref _transferBytesLive);
    
    public bool TransferMeasured => Volatile.Read(ref _transferReads) > 0;
    
    public double? TransferMs => TransferMeasured
        ? Volatile.Read(ref _transferMicroseconds) / 1000.0
        : null;
    
    public double? DecodeMs => CompleteMs is { } total && TransferMs is { } transfer
        ? Math.Max(0, total - transfer)
        : null;

    /// <summary>
    /// The duration worth learning from, and whether it includes the read.
    /// </summary>
    public (double Ms, bool IncludesTransfer)? Learnable => DecodeMs is { } decode
        ? (decode, false)
        : CompleteMs is { } total
            ? (total, true)
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
    }
    
    internal void ReportTransferred(long bytesSoFar) => Volatile.Write(ref _transferBytesLive, bytesSoFar);

    internal void CompleteTransfer(long bytes, double ms)
    {
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