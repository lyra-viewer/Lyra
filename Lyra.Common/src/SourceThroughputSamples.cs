using System.Collections.Concurrent;

namespace Lyra.Common;

/// <summary>How long fetching a file from some storage is expected to take.</summary>
/// <param name="LatencyMs">Fixed cost per file, before any bytes move. Dominates small files on a share.</param>
/// <param name="BytesPerMs">Rate once bytes are moving.</param>
public readonly record struct TransferEstimate(double LatencyMs, double BytesPerMs)
{
    /// <summary>The predicted wait for a file of this size.</summary>
    public double MsFor(long bytes) => BytesPerMs > 0 ? LatencyMs + bytes / BytesPerMs : LatencyMs;
}

/// <summary>
/// Measured read speed per storage source, used to predict what fetching a file will cost.
/// </summary>
public sealed class SourceThroughputSamples
{
    /// <summary>Rolling window per source, so a mount that gets slower is believed reasonably soon.</summary>
    private const int MaxSamplesPerSource = 24;

    /// <summary>Below this there is nothing to fit a two-term model to.</summary>
    private const int MinSamplesToEstimate = 3;

    /// <summary>
    /// No storage device delivers this, so anything above it came from the operating system's
    /// page cache rather than the source. Accepting those would teach a slow share that it is
    /// fast - the one error that matters, since it is what the estimate exists to catch.
    /// </summary>
    private const double ImplausibleBytesPerMs = 8.0 * 1024 * 1024; // 8 GB/s

    /// <summary>How many recently-read files to remember, to avoid sampling a re-read.</summary>
    private const int RecentFilesRemembered = 4096;

    private readonly record struct Sample(long Bytes, double Ms);

    private readonly ConcurrentDictionary<string, List<Sample>> _bySource = new(StringComparer.OrdinalIgnoreCase);

    private readonly Func<string, string> _sourceOf;

    private readonly HashSet<string> _sampledFiles = new(StringComparer.OrdinalIgnoreCase);
    private readonly Queue<string> _sampledOrder = new();
    private readonly Lock _sampledLock = new();

    public SourceThroughputSamples() : this(StorageSource.RootFor) { }

    public SourceThroughputSamples(Func<string, string> sourceOf) => _sourceOf = sourceOf;

    /// <summary>
    /// Records one file's read. Ignored when the numbers cannot be a real read of the source: a
    /// non-positive duration, an impossible rate, or a file already sampled this session, whose
    /// second read is likely served from cache at a speed the source cannot sustain.
    /// </summary>
    public void Record(string path, long bytes, double ms)
    {
        if (bytes <= 0 || ms <= 0 || string.IsNullOrEmpty(path))
            return;

        var rate = bytes / ms;
        if (rate > ImplausibleBytesPerMs)
        {
            Logger.Debug($"[SourceThroughput] Ignoring {rate * 1000 / (1024 * 1024):F0} MB/s - that is cache, not the source: {path}");
            return;
        }

        if (!FirstReadThisSession(path))
            return;

        var source = _sourceOf(path);
        var samples = _bySource.GetOrAdd(source, _ => []);

        lock (samples)
        {
            samples.Add(new Sample(bytes, ms));
            if (samples.Count > MaxSamplesPerSource)
                samples.RemoveAt(0);
        }
    }

    /// <summary>
    /// How this path's storage behaves, or null while too little has been read from it to say.
    /// Ask the result what a particular file size will cost.
    /// </summary>
    public TransferEstimate? Estimate(string path)
    {
        if (string.IsNullOrEmpty(path) || !_bySource.TryGetValue(_sourceOf(path), out var samples))
            return null;

        Sample[] snapshot;
        lock (samples)
        {
            if (samples.Count < MinSamplesToEstimate)
                return null;

            snapshot = samples.ToArray();
        }

        return Fit(snapshot);
    }

    private static TransferEstimate? Fit(Sample[] samples)
    {
        var slopes = new List<double>();
        for (var i = 0; i < samples.Length; i++)
        for (var j = i + 1; j < samples.Length; j++)
        {
            var byteGap = samples[j].Bytes - samples[i].Bytes;
            if (byteGap != 0)
                slopes.Add((samples[j].Ms - samples[i].Ms) / byteGap);
        }

        var slope = Median(slopes);
        if (slope <= 0)
            return RateOnly(samples);

        var latency = Median(samples.Select(s => s.Ms - slope * s.Bytes));
        return new TransferEstimate(Math.Max(0, latency), Plausible(1 / slope));
    }

    private static TransferEstimate? RateOnly(Sample[] samples)
    {
        var rate = Median(samples.Select(s => s.Bytes / s.Ms));
        return rate > 0 ? new TransferEstimate(0, Plausible(rate)) : null;
    }

    private static double Plausible(double bytesPerMs) => Math.Min(bytesPerMs, ImplausibleBytesPerMs);

    private static double Median(IEnumerable<double> values)
    {
        var sorted = values.ToArray();
        if (sorted.Length == 0)
            return 0;

        Array.Sort(sorted);
        var mid = sorted.Length / 2;
        return sorted.Length % 2 == 1 ? sorted[mid] : (sorted[mid - 1] + sorted[mid]) / 2;
    }

    /// <summary>
    /// Whether this file has not been read yet this session. A second read of the same file
    /// usually comes from the page cache at a speed that says nothing about the source.
    /// </summary>
    private bool FirstReadThisSession(string path)
    {
        lock (_sampledLock)
        {
            if (!_sampledFiles.Add(path))
                return false;

            _sampledOrder.Enqueue(path);
            if (_sampledOrder.Count > RecentFilesRemembered)
                _sampledFiles.Remove(_sampledOrder.Dequeue());

            return true;
        }
    }
}