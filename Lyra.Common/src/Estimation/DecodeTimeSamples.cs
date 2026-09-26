using System.Collections.Concurrent;
using System.Globalization;
using System.Numerics;
using Tomlyn;
using Tomlyn.Model;
using Tomlyn.Parsing;

namespace Lyra.Common.Estimation;

/// <summary>
/// A rolling history of decode durations, bucketed by format and by the amount of work decode
/// represented, persisted as TOML.
///
/// Decode only: the time spent fetching the file is measured separately and excluded, because it
/// belongs to the source rather than the format.
/// </summary>
public sealed class DecodeTimeSamples
{
    private const int SchemaVersion = 4;

    private const string VersionKey = "version";

    /// <summary>
    /// How many loads may go unsaved before the history is written out.
    /// </summary>
    private const int UnsavedChangesThreshold = 3;

    /// <summary>In-memory history per bucket (rolling).</summary>
    private const int MaxSamplesPerBucket = 20;

    /// <summary>Persisted samples per bucket (compact, representative).</summary>
    private const int PersistedSamplesPerBucket = 7;

    /// <summary>Bucket 1 is everything up to 256 KB, then 512 KB, 1 MB, 2 MB, and so on.</summary>
    private const long BytesPerBucketUnit = 256_000;

    /// <summary>Bucket 1 is everything up to 256x256, then 512x256, 512x512, and so on.</summary>
    private const long PixelsPerBucketUnit = 65_536;

    /// <summary>
    /// A ceiling on what may be predicted. Scaling from a distant bucket is a straight line
    /// through evidence gathered somewhere else on the curve, and a bad one must degrade into a
    /// bar that finishes late rather than one that claims an hour.
    /// </summary>
    private const double MaxEstimateMs = 10 * 60 * 1000;

    private const string BytesTableKey = "bytes";
    private const string PixelsTableKey = "pixels";
    
    private enum Metric
    {
        Bytes,
        Pixels
    }

    private readonly string _filePath;

    private readonly ConcurrentDictionary<(string Format, Metric Metric, int Bucket), List<double>> _samples = new();

    private readonly Lock _saveLock = new();

    private int _unsavedChanges;

    public DecodeTimeSamples(string filePath)
    {
        _filePath = filePath;
        Load();
    }
    
    public void Record(string extension, long sizeInBytes, long? pixels, double ms)
    {
        if (ms <= 0 || !TryGetFormat(extension, out var format))
            return;

        RecordSample(format, Metric.Bytes, Bucket(sizeInBytes, BytesPerBucketUnit), sizeInBytes, ms);

        if (pixels is > 0)
            RecordSample(format, Metric.Pixels, Bucket(pixels.Value, PixelsPerBucketUnit), pixels.Value, ms);

        if (Interlocked.Increment(ref _unsavedChanges) >= UnsavedChangesThreshold)
        {
            Interlocked.Exchange(ref _unsavedChanges, 0);
            Save(suppressLogging: true);
        }
    }

    private void RecordSample(string format, Metric metric, int bucket, long magnitude, double ms)
    {
        var list = _samples.GetOrAdd((format, metric, bucket), _ => []);
        lock (list)
        {
            list.Add(ms);
            Logger.Debug($"[DecodeTimeSamples] Recorded: {format}, {magnitude} {metric.ToString().ToLowerInvariant()}, {Formatters.MsToStr(ms)} ms.");

            if (list.Count > MaxSamplesPerBucket)
                list.RemoveAt(0);
        }
    }

    /// <summary>
    /// What this format and this much work should take. Pixels answer when the caller has them,
    /// since they predict far better than compressed bytes do; otherwise the byte-keyed history
    /// carries the estimate until a decoder reads the header and comes back with the real size.
    /// </summary>
    public LoadEstimate Estimate(string extension, long sizeInBytes, long? pixels = null)
    {
        if (!TryGetFormat(extension, out var format))
            return LoadEstimate.None;

        if (pixels is > 0)
        {
            var byPixels = EstimateFor(format, Metric.Pixels, pixels.Value, PixelsPerBucketUnit);
            if (byPixels > 0)
                return new LoadEstimate(byPixels);
        }

        return new LoadEstimate(EstimateFor(format, Metric.Bytes, sizeInBytes, BytesPerBucketUnit));
    }

    private double EstimateFor(string format, Metric metric, long magnitude, long unit)
    {
        var bucket = Bucket(magnitude, unit);

        if (_samples.TryGetValue((format, metric, bucket), out var exact))
            return Typical(exact);

        var nearest = NearestBucket(format, metric, bucket);
        if (nearest <= 0 || !_samples.TryGetValue((format, metric, nearest), out var fallback))
            return 0;

        var typical = Typical(fallback);
        if (typical <= 0)
            return 0;

        return Math.Min(MaxEstimateMs, typical * bucket / nearest);
    }
    
    private int NearestBucket(string format, Metric metric, int bucket)
    {
        var target = BitOperations.Log2((uint)bucket);

        var best = -1;
        var bestDistance = int.MaxValue;

        foreach (var key in _samples.Keys)
        {
            if (key.Metric != metric || !key.Format.Equals(format, StringComparison.OrdinalIgnoreCase))
                continue;

            var distance = Math.Abs(BitOperations.Log2((uint)key.Bucket) - target);
            if (distance < bestDistance || (distance == bestDistance && key.Bucket > best))
            {
                bestDistance = distance;
                best = key.Bucket;
            }
        }

        return best;
    }

    /// <summary>
    /// The median, not the mean. These are latencies: one read that hit a stalled mount or one
    /// decode that lost its core to a preload skews an average for the whole life of the bucket,
    /// and the estimate it feeds is a duration a person is waiting on.
    /// </summary>
    private static double Typical(List<double> samples)
    {
        double[] sorted;
        lock (samples)
        {
            if (samples.Count == 0)
                return 0;

            sorted = samples.ToArray();
        }

        Array.Sort(sorted);
        return Quantile(sorted, 0.50);
    }

    public void Save(bool suppressLogging = false)
    {
        try
        {
            var toml = Serialize();

            var dir = Path.GetDirectoryName(_filePath);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);

            // Atomic-ish write: temp then replace.
            var tmp = _filePath + ".tmp";
            lock (_saveLock)
            {
                File.WriteAllText(tmp, toml);

                if (File.Exists(_filePath))
                    File.Replace(tmp, _filePath, destinationBackupFileName: null);
                else
                    File.Move(tmp, _filePath);
            }

            if (!suppressLogging)
                Logger.Info("[DecodeTimeSamples] Successfully saved time data.");
        }
        catch (Exception ex)
        {
            Logger.Error($"[DecodeTimeSamples] Failed to save time data: {ex.Message}");
        }
    }

    internal string Serialize()
    {
        var root = new TomlTable
        {
            [VersionKey] = SchemaVersion
        };

        var snapshot = new Dictionary<(string Format, Metric Metric, int Bucket), List<double>>();
        foreach (var entry in _samples)
        {
            var list = entry.Value;
            lock (list)
                snapshot[entry.Key] = list.ToList();
        }

        foreach (var formatGroup in snapshot
                     .GroupBy(x => x.Key.Format, StringComparer.OrdinalIgnoreCase)
                     .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase))
        {
            var formatTable = new TomlTable();

            foreach (var metric in (Metric[])[Metric.Bytes, Metric.Pixels])
            {
                var metricTable = new TomlTable();
                var wroteBucket = false;

                foreach (var bucketEntry in formatGroup.Where(x => x.Key.Metric == metric).OrderBy(x => x.Key.Bucket))
                {
                    var samples = bucketEntry.Value;
                    if (samples.Count == 0)
                        continue;

                    var arr = new TomlArray();
                    foreach (var v in SelectRepresentativeSamples(samples, PersistedSamplesPerBucket))
                        arr.Add((int)Math.Round(v, MidpointRounding.AwayFromZero));

                    metricTable[bucketEntry.Key.Bucket.ToString(CultureInfo.InvariantCulture)] = arr;
                    wroteBucket = true;
                }

                if (wroteBucket)
                    formatTable[MetricKey(metric)] = metricTable;
            }

            if (formatTable.Count > 0)
                root[formatGroup.Key.ToLowerInvariant()] = formatTable;
        }

        return TomlSerializer.Serialize(root, LyraTomlContext.Default.TomlTable);
    }

    private void Load()
    {
        if (!File.Exists(_filePath))
        {
            Logger.Info("[DecodeTimeSamples] No existing time data found.");
            return;
        }

        try
        {
            Deserialize(File.ReadAllText(_filePath));
            Logger.Info("[DecodeTimeSamples] Successfully loaded time data.");
        }
        catch (Exception ex)
        {
            Logger.Error($"[DecodeTimeSamples] Failed to load time data: {ex.Message}");
        }
    }

    internal void Deserialize(string text)
    {
        var doc = SyntaxParser.Parse(text);
        if (doc.HasErrors)
        {
            Logger.Error("[DecodeTimeSamples] Failed to load time data: TOML parse errors.");
            return;
        }

        var model = TomlSerializer.Deserialize(text, LyraTomlContext.Default.TomlTable)!;
        var version = model.TryGetValue(VersionKey, out var value) ? Convert.ToInt32(value) : 0;

        if (version == SchemaVersion)
        {
            ReadFormats(model);
            return;
        }

        Logger.Info($"[DecodeTimeSamples] Time data is schema {version}, not {SchemaVersion}; starting fresh.");
        _samples.Clear();
    }

    private void ReadFormats(TomlTable model)
    {
        _samples.Clear();

        foreach (var formatEntry in model)
        {
            if (formatEntry.Value is not TomlTable metricsTable)
                continue; // The version key, or anything else that is not a format table.

            var format = formatEntry.Key.ToLowerInvariant();

            foreach (var metricEntry in metricsTable)
            {
                if (metricEntry.Value is not TomlTable bucketsTable || !TryReadMetric(metricEntry.Key, out var metric))
                    continue;

                ReadBuckets(format, metric, bucketsTable);
            }
        }
    }

    private void ReadBuckets(string format, Metric metric, TomlTable bucketsTable)
    {
        foreach (var bucketEntry in bucketsTable)
        {
            if (!int.TryParse(bucketEntry.Key, NumberStyles.Integer, CultureInfo.InvariantCulture, out var bucket) || bucket <= 0)
                continue; // Anything that is not a size bucket.

            if (bucketEntry.Value is not TomlArray arr)
                continue;

            var list = new List<double>(arr.Count);
            foreach (var v in arr)
            {
                switch (v)
                {
                    case double d and > 0: list.Add(d); break;
                    case float f and > 0: list.Add(f); break;
                    case long l and > 0: list.Add(l); break;
                    case int i and > 0: list.Add(i); break;
                }
            }

            if (list.Count == 0)
                continue;

            // Loaded samples become our rolling history; cap it.
            if (list.Count > MaxSamplesPerBucket)
                list = list.Skip(list.Count - MaxSamplesPerBucket).ToList();

            _samples[(format, metric, bucket)] = list;
        }
    }

    private static string MetricKey(Metric metric) => metric == Metric.Pixels ? PixelsTableKey : BytesTableKey;

    private static bool TryReadMetric(string key, out Metric metric)
    {
        switch (key.ToLowerInvariant())
        {
            case BytesTableKey:
                metric = Metric.Bytes;
                return true;
            case PixelsTableKey:
                metric = Metric.Pixels;
                return true;
            default:
                metric = default;
                return false;
        }
    }

    /// <summary>Bucket sizes: 256KB, 512KB, 1MB, 2MB, 4MB, and so on.</summary>
    private static int Bucket(long magnitude, long unit)
    {
        if (magnitude <= unit)
            return 1;

        var exponent = (int)Math.Ceiling(Math.Log2((double)magnitude / unit));
        return 1 << Math.Clamp(exponent, 0, 30);
    }

    private static bool TryGetFormat(string extension, out string format)
    {
        format = string.Empty;

        var formatType = ImageFormat.GetImageFormat(extension);
        if (formatType == ImageFormatType.Unknown)
            return false;

        format = formatType.ToString().ToLowerInvariant();
        return true;
    }

    private static double[] SelectRepresentativeSamples(List<double> samples, int targetCount)
    {
        if (samples.Count == 0)
            return [];

        var sorted = samples.OrderBy(x => x).ToArray();

        if (sorted.Length <= targetCount)
            return sorted;

        if (targetCount == 7)
        {
            return
            [
                sorted[0],
                Quantile(sorted, 0.10),
                Quantile(sorted, 0.25),
                Quantile(sorted, 0.50),
                Quantile(sorted, 0.75),
                Quantile(sorted, 0.90),
                sorted[^1]
            ];
        }

        var result = new double[targetCount];
        for (var i = 0; i < targetCount; i++)
        {
            var q = (double)i / (targetCount - 1);
            result[i] = Quantile(sorted, q);
        }

        return result;
    }

    private static double Quantile(double[] sorted, double q)
    {
        if (sorted.Length == 0)
            return 0;

        if (q <= 0)
            return sorted[0];

        if (q >= 1)
            return sorted[^1];

        var pos = (sorted.Length - 1) * q;
        var i = (int)Math.Floor(pos);
        var frac = pos - i;

        if (i >= sorted.Length - 1)
            return sorted[^1];

        return sorted[i] + (sorted[i + 1] - sorted[i]) * frac;
    }
}