using System.Buffers;
using System.IO.Hashing;
using Lyra.Common;
using Lyra.FileLoader.Store;

namespace Lyra.FileLoader.Duplicates.Exact;

public static class DuplicateFinder
{
    private const int BufferSize = 1 << 16; // 64 KB

    public static IReadOnlyList<DuplicateGroup> Scan(IProgress<DuplicateScanProgress>? progress = null, CancellationToken ct = default)
    {
        var records = FileRecordDatabase.Records.ToArray();

        EnsureSizes(records, progress, ct);
        var candidates = GatherSizeCandidates(records);
        HashCandidates(records, candidates, progress, ct);

        return GroupByContentHash(records, candidates);
    }

    private static void EnsureSizes(FileRecord[] records, IProgress<DuplicateScanProgress>? progress, CancellationToken ct)
    {
        for (var i = 0; i < records.Length; i++)
        {
            ct.ThrowIfCancellationRequested();

            if (!records[i].HasSize)
            {
                var size = TryGetSize(records[i].Path);
                if (size > 0)
                {
                    records[i] = records[i] with { Size = size };
                    FileRecordDatabase.SetSize(records[i].Path, size);
                }
            }

            progress?.Report(new DuplicateScanProgress(i + 1, 0, 0));
        }
    }

    private static List<int> GatherSizeCandidates(FileRecord[] records)
    {
        var bySize = new Dictionary<long, List<int>>();
        for (var i = 0; i < records.Length; i++)
        {
            // Skip zero-byte and still-unknown sizes (both are 0 by convention).
            if (records[i].Size <= 0)
                continue;

            if (!bySize.TryGetValue(records[i].Size, out var list))
                bySize[records[i].Size] = list = [];

            list.Add(i);
        }

        return bySize.Values.Where(g => g.Count > 1).SelectMany(g => g).ToList();
    }

    private static void HashCandidates(FileRecord[] records, List<int> candidates, IProgress<DuplicateScanProgress>? progress, CancellationToken ct)
    {
        var hashed = 0;
        foreach (var i in candidates)
        {
            ct.ThrowIfCancellationRequested();

            if (!records[i].HasContentHash)
            {
                var hash = TryHashFile(records[i].Path, ct);
                if (hash is { } value)
                {
                    records[i] = records[i] with { ContentHash = value };
                    FileRecordDatabase.SetContentHash(records[i].Path, value);
                }
            }

            hashed++;
            progress?.Report(new DuplicateScanProgress(FilesSized: 0, hashed, candidates.Count));
        }
    }

    private static List<DuplicateGroup> GroupByContentHash(FileRecord[] records, List<int> candidates)
    {
        var byHash = new Dictionary<(long Size, UInt128 Hash), List<FileRecord>>();
        foreach (var i in candidates)
        {
            var record = records[i];
            if (record.Size <= 0 || !record.HasContentHash)
                continue;

            var key = (record.Size, record.ContentHash);
            if (!byHash.TryGetValue(key, out var list))
                byHash[key] = list = [];

            list.Add(record);
        }

        return byHash
            .Where(kv => kv.Value.Count > 1)
            .Select(kv => new DuplicateGroup(kv.Key.Size, kv.Key.Hash, kv.Value))
            .ToList();
    }

    private static long TryGetSize(string path)
    {
        try
        {
            return new FileInfo(path).Length;
        }
        catch (Exception ex)
        {
            Logger.Warning($"[DuplicateFinder] Size failed for '{path}': {ex.Message}");
            return 0;
        }
    }

    private static UInt128? TryHashFile(string path, CancellationToken ct)
    {
        var buffer = ArrayPool<byte>.Shared.Rent(BufferSize);
        try
        {
            using var stream = File.OpenRead(path);
            var hasher = new XxHash128();

            int read;
            while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
            {
                ct.ThrowIfCancellationRequested();
                hasher.Append(buffer.AsSpan(0, read));
            }

            return hasher.GetCurrentHashAsUInt128();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            Logger.Warning($"[DuplicateFinder] Hash failed for '{path}': {ex.Message}");
            return null;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }
}