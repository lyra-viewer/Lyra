using System.Collections.Concurrent;
using System.Globalization;
using Tomlyn;
using Tomlyn.Model;

namespace Lyra.Common;

public static class LoadTimeEstimator
{
    private static readonly string TimeFilePath = LyraIO.GetLoadTimeFile();

    private static readonly ConcurrentDictionary<(string Format, int SizeBucket), List<double>> LoadTimeData = new();

    private const int UnsavedChangesThreshold = 5;

    // In-memory history per bucket (rolling).
    private const int MaxSamplesPerBucket = 20;

    // Persisted samples per bucket (compact, representative).
    private const int PersistedSamplesPerBucket = 7;

    private static int _unsavedChangesCount;

    static LoadTimeEstimator()
    {
        LoadTimeDataFromFile();
    }

    public static void RecordLoadTime(string extension, long sizeInBytes, double loadTime)
    {
        if (!TryGetKey(extension, sizeInBytes, out var key))
            return;

        var list = LoadTimeData.GetOrAdd(key, _ => []);
        lock (list)
        {
            list.Add(loadTime);
            Logger.Debug($"[LoadTimeEstimator] Recorded load time: {key.Format}, {sizeInBytes} bytes, {loadTime} ms.");

            if (list.Count > MaxSamplesPerBucket)
                list.RemoveAt(0);
        }

        // Save periodically
        if (++_unsavedChangesCount >= UnsavedChangesThreshold)
        {
            SaveTimeDataToFile(true);
            _unsavedChangesCount = 0;
        }
    }

    public static double EstimateLoadTime(string extension, long sizeInBytes)
    {
        if (!TryGetKey(extension, sizeInBytes, out var key))
            return 0;

        if (LoadTimeData.TryGetValue(key, out var loadTimes))
        {
            // Direct match found, return the average
            lock (loadTimes)
                return loadTimes.Count == 0 ? 0 : loadTimes.Average();
        }

        // No exact match: Find closest available bucket for this format
        var availableBuckets = LoadTimeData.Keys
            .Where(k => k.Format.Equals(key.Format, StringComparison.OrdinalIgnoreCase))
            .Select(k => k.SizeBucket)
            .Distinct()
            .OrderBy(b => Math.Abs(b - key.SizeBucket))
            .ToList();

        if (availableBuckets.Count == 0)
            return 0;

        var closestBucket = availableBuckets.First();
        var fallbackKey = (key.Format, closestBucket);

        if (LoadTimeData.TryGetValue(fallbackKey, out var fallbackTimes))
            lock (fallbackTimes)
                return fallbackTimes.Count == 0 ? 0 : fallbackTimes.Average();

        return 0;
    }

    public static void SaveTimeDataToFile(bool suppressLogging = false)
    {
        try
        {
            var root = new TomlTable();

            // snapshot
            var snapshot = new Dictionary<(string Format, int Bucket), List<double>>();
            foreach (var entry in LoadTimeData)
            {
                var list = entry.Value;
                lock (list)
                    snapshot[entry.Key] = list.ToList();
            }

            foreach (var formatGroup in snapshot
                         .GroupBy(x => x.Key.Format, StringComparer.OrdinalIgnoreCase)
                         .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase))
            {
                var formatKey = formatGroup.Key.ToLowerInvariant();

                var formatTable = new TomlTable();
                root[formatKey] = formatTable;

                foreach (var bucketEntry in formatGroup.OrderBy(x => x.Key.Bucket))
                {
                    var bucket = bucketEntry.Key.Bucket;
                    var samples = bucketEntry.Value;

                    if (samples.Count == 0)
                        continue;

                    var persisted = SelectRepresentativeSamples(samples, PersistedSamplesPerBucket);

                    var arr = new TomlArray();
                    foreach (var v in persisted)
                        arr.Add((int)Math.Round(v, MidpointRounding.AwayFromZero));

                    formatTable[bucket.ToString(CultureInfo.InvariantCulture)] = arr;
                }
            }

            var toml = Toml.FromModel(root);

            var dir = Path.GetDirectoryName(TimeFilePath);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);

            // Atomic-ish write: temp then replace
            var tmp = TimeFilePath + ".tmp";
            File.WriteAllText(tmp, toml);

            if (File.Exists(TimeFilePath))
                File.Replace(tmp, TimeFilePath, destinationBackupFileName: null);
            else
                File.Move(tmp, TimeFilePath);

            if (!suppressLogging)
                Logger.Info("[LoadTimeEstimator] Successfully saved time data.");
        }
        catch (Exception ex)
        {
            Logger.Error($"[LoadTimeEstimator] Failed to save time data: {ex.Message}");
        }
    }

    private static void LoadTimeDataFromFile()
    {
        if (!File.Exists(TimeFilePath))
        {
            Logger.Info("[LoadTimeEstimator] No existing time data found.");
            return;
        }

        try
        {
            var text = File.ReadAllText(TimeFilePath);
            var doc = Toml.Parse(text);
            if (doc.HasErrors)
            {
                Logger.Error("[LoadTimeEstimator] Failed to load time data: TOML parse errors.");
                return;
            }

            var model = doc.ToModel(); // TomlTable
            LoadTimeData.Clear();

            foreach (var formatEntry in model)
            {
                if (formatEntry.Value is not TomlTable bucketsTable)
                    continue;

                var format = formatEntry.Key.ToLowerInvariant();

                foreach (var bucketEntry in bucketsTable)
                {
                    if (!int.TryParse(bucketEntry.Key, NumberStyles.Integer, CultureInfo.InvariantCulture, out var bucket) || bucket <= 0)
                        continue;

                    if (bucketEntry.Value is not TomlArray arr)
                        continue;

                    var list = new List<double>(arr.Count);
                    foreach (var v in arr)
                    {
                        if (v is double d and > 0)
                            list.Add(d);
                        else if (v is float f and > 0)
                            list.Add(f);
                        else if (v is long l and > 0)
                            list.Add(l);
                        else if (v is int i and > 0)
                            list.Add(i);
                    }

                    if (list.Count == 0)
                        continue;

                    // Loaded samples become our rolling history; cap it.
                    if (list.Count > MaxSamplesPerBucket)
                        list = list.Skip(list.Count - MaxSamplesPerBucket).ToList();

                    LoadTimeData[(format, bucket)] = list;
                }
            }

            Logger.Info("[LoadTimeEstimator] Successfully loaded time data.");
        }
        catch (Exception ex)
        {
            Logger.Error($"[LoadTimeEstimator] Failed to load time data: {ex.Message}");
        }
    }

    private static int GetSizeBucket(long sizeInBytes)
    {
        // Bucket sizes: 256KB, 512KB, 1MB, 2MB, 4MB, etc.
        var bucket = (int)Math.Pow(2, Math.Ceiling(Math.Log(sizeInBytes / 256000.0, 2)));
        return Math.Max(bucket, 1); // Ensure minimum bucket of 1
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

    /// <summary>
    /// Persist a compact representative set from a rolling history:
    /// default target=7: min, p10, p25, p50, p75, p90, max
    /// </summary>
    private static double[] SelectRepresentativeSamples(List<double> samples, int targetCount)
    {
        if (samples.Count == 0)
            return Array.Empty<double>();

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