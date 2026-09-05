using System.Collections.Concurrent;
using System.Globalization;
using Tomlyn;
using Tomlyn.Model;
using Tomlyn.Parsing;

namespace Lyra.Common;

/// <summary>
/// A rolling history of decode durations, bucketed by format and file size, persisted as TOML.
///
/// Decode only: the time spent fetching the file is measured separately and excluded, because it
/// belongs to the source rather than the format.
/// </summary>
public sealed class DecodeTimeSamples
{
    private const int SchemaVersion = 2;

    private const string VersionKey = "version";

    private const int UnsavedChangesThreshold = 5;

    /// <summary>In-memory history per bucket (rolling).</summary>
    private const int MaxSamplesPerBucket = 20;

    /// <summary>Persisted samples per bucket (compact, representative).</summary>
    private const int PersistedSamplesPerBucket = 7;

    private readonly string _filePath;

    private readonly ConcurrentDictionary<(string Format, int SizeBucket), List<double>> _samples = new();

    private readonly Lock _saveLock = new();

    private int _unsavedChanges;

    public DecodeTimeSamples(string filePath)
    {
        _filePath = filePath;
        Load();
    }
    
    public void Record(string extension, long sizeInBytes, double ms)
    {
        if (ms <= 0 || !TryGetKey(extension, sizeInBytes, out var key))
            return;

        var list = _samples.GetOrAdd(key, _ => []);
        lock (list)
        {
            list.Add(ms);
            Logger.Debug($"[DecodeTimeSamples] Recorded: {key.Format}, {sizeInBytes} bytes, {ms} ms.");

            if (list.Count > MaxSamplesPerBucket)
                list.RemoveAt(0);
        }

        if (Interlocked.Increment(ref _unsavedChanges) >= UnsavedChangesThreshold)
        {
            Interlocked.Exchange(ref _unsavedChanges, 0);
            Save(suppressLogging: true);
        }
    }

    /// <summary>
    /// What this format and size should take. Falls back to the nearest size bucket for the same
    /// format, since a format's cost per byte varies far less than it does between formats.
    /// </summary>
    public double Estimate(string extension, long sizeInBytes)
    {
        if (!TryGetKey(extension, sizeInBytes, out var key))
            return 0;

        if (_samples.TryGetValue(key, out var exact))
            return Typical(exact);

        var nearest = _samples.Keys
            .Where(k => k.Format.Equals(key.Format, StringComparison.OrdinalIgnoreCase))
            .Select(k => k.SizeBucket)
            .DefaultIfEmpty(-1)
            .MinBy(bucket => Math.Abs(bucket - key.SizeBucket));

        return nearest >= 0 && _samples.TryGetValue((key.Format, nearest), out var fallback)
            ? Typical(fallback)
            : 0;
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

        var snapshot = new Dictionary<(string Format, int Bucket), List<double>>();
        foreach (var entry in _samples)
        {
            var list = entry.Value;
            lock (list)
                snapshot[entry.Key] = list.ToList();
        }

        foreach (var formatGroup in snapshot
                     .GroupBy(x => x.Key.Format, StringComparer.OrdinalIgnoreCase)
                     .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
                )
        {
            var formatKey = formatGroup.Key.ToLowerInvariant();

            var formatTable = new TomlTable();
            var wroteBucket = false;

            foreach (var bucketEntry in formatGroup.OrderBy(x => x.Key.Bucket))
            {
                var samples = bucketEntry.Value;
                if (samples.Count == 0)
                    continue;

                var arr = new TomlArray();
                foreach (var v in SelectRepresentativeSamples(samples, PersistedSamplesPerBucket))
                    arr.Add((int)Math.Round(v, MidpointRounding.AwayFromZero));

                formatTable[bucketEntry.Key.Bucket.ToString(CultureInfo.InvariantCulture)] = arr;
                wroteBucket = true;
            }

            if (wroteBucket)
                root[formatKey] = formatTable;
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
        if (model.TryGetValue(VersionKey, out var version) && Convert.ToInt32(version) == SchemaVersion)
        {
            ReadFormats(model);
            return;
        }

        Logger.Info("[DecodeTimeSamples] Time data is from an older schema; starting fresh.");
        _samples.Clear();
    }

    private void ReadFormats(TomlTable model)
    {
        _samples.Clear();

        foreach (var formatEntry in model)
        {
            if (formatEntry.Value is not TomlTable bucketsTable)
                continue; // The version key, or anything else that is not a format table.

            var format = formatEntry.Key.ToLowerInvariant();

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

                _samples[(format, bucket)] = list;
            }
        }
    }

    /// <summary>Bucket sizes: 256KB, 512KB, 1MB, 2MB, 4MB, and so on.</summary>
    private static int GetSizeBucket(long sizeInBytes)
    {
        if (sizeInBytes <= 0)
            return 1;

        var bucket = (int)Math.Pow(2, Math.Ceiling(Math.Log(sizeInBytes / 256000.0, 2)));
        return Math.Max(bucket, 1);
    }

    private static bool TryGetKey(string extension, long sizeInBytes, out (string Format, int SizeBucket) key)
    {
        key = default;

        var formatType = ImageFormat.GetImageFormat(extension);
        if (formatType == ImageFormatType.Unknown)
            return false;

        key = (formatType.ToString().ToLowerInvariant(), GetSizeBucket(sizeInBytes));
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