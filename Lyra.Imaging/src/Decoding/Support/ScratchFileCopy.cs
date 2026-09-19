using System.Diagnostics;
using Lyra.Common;

namespace Lyra.Imaging.Decoding.Support;

/// <summary>
/// A local copy of a file too large to buffer in memory, for handing to a native decoder that only
/// takes a path and does its own blocking IO with no cancellation hook. The copy itself happens in
/// managed, chunked reads that check the cancellation token between chunks, so abandoning a slow
/// network transfer is prompt even though the native decode that follows is not cancellable.
/// </summary>
internal sealed class ScratchFileCopy : IDisposable
{
    /// <summary>Free space kept clear beyond the file itself, so scratch use never starves the disk.</summary>
    private const long SafetyMarginBytes = 512L * 1024 * 1024;

    /// <summary>Total scratch bytes this process will reserve across concurrent jobs.</summary>
    private const long MaxConcurrentReservationBytes = 8L * 1024 * 1024 * 1024;

    private static long _reservedBytes;

    private readonly long _reservedForThis;
    private bool _disposed;

    public string Path { get; }
    
    public long BytesCopied { get; private init; }

    private ScratchFileCopy(string path, long reservedForThis)
    {
        Path = path;
        _reservedForThis = reservedForThis;
    }
    
    public static ScratchFileCopy? TryCreate(string sourcePath, long sizeBytes, CancellationToken ct, out double elapsedMs, Action<long>? onProgress = null)
    {
        elapsedMs = 0;

        if (sizeBytes <= 0)
            return null;
        
        if (!TryGetScratchDir(out var dir))
            return null;

        if (!TryReserve(sizeBytes))
            return null;

        var scratchPath = System.IO.Path.Combine(dir, $"tiff-{Environment.ProcessId}-{Guid.NewGuid():N}.tmp");

        try
        {
            if (!HasRoom(dir, sizeBytes))
            {
                Release(sizeBytes);
                return null;
            }

            var start = Stopwatch.GetTimestamp();
            long total = 0;

            using (var source = DecoderIO.OpenSequentialRead(sourcePath))
            using (var dest = new FileStream(scratchPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, DecoderIO.ReadChunk, FileOptions.SequentialScan))
            {
                var buffer = new byte[DecoderIO.ReadChunk];

                while (true)
                {
                    ct.ThrowIfCancellationRequested();

                    var read = source.Read(buffer, 0, buffer.Length);
                    if (read == 0)
                        break;

                    dest.Write(buffer, 0, read);
                    total += read;
                    onProgress?.Invoke(total);
                }
            }

            elapsedMs = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
            Logger.Debug($"[ScratchFileCopy] Copied {total / (1024 * 1024)} MB to {scratchPath} in {elapsedMs:F0} ms for cancellable native decode.");
            return new ScratchFileCopy(scratchPath, sizeBytes) { BytesCopied = total };
        }
        catch (OperationCanceledException)
        {
            TryDelete(scratchPath);
            Release(sizeBytes);
            throw;
        }
        catch (Exception ex)
        {
            TryDelete(scratchPath);
            Release(sizeBytes);

            Logger.Info($"[ScratchFileCopy] Could not copy {sourcePath} to {dir}: {ex.Message}; decoding from the source path instead.");
            return null;
        }
    }

    /// <summary>
    /// Deletes scratch files left behind by a run that never got to clean up after itself - a hard
    /// kill or crash between finishing a copy and disposing it.
    /// </summary>
    public static void SweepStaleFiles()
    {
        if (!TryGetScratchDir(out var dir))
            return;

        try
        {
            foreach (var file in Directory.EnumerateFiles(dir, "tiff-*.tmp"))
            {
                if (BelongsToLiveProcess(file))
                    continue;

                TryDelete(file);
            }
        }
        catch (Exception ex)
        {
            Logger.Debug($"[ScratchFileCopy] Could not sweep {dir}: {ex.Message}");
        }
    }

    /// <summary>
    /// Whether this scratch file belongs to a process still running, read from the process id its
    /// name carries. Deleting a live instance's copy would fail that instance's decode, because the
    /// file is opened again by path once the copy stream closes.
    /// </summary>
    private static bool BelongsToLiveProcess(string file)
    {
        var parts = System.IO.Path.GetFileNameWithoutExtension(file).Split('-');

        if (parts.Length < 3 || !int.TryParse(parts[1], out var pid))
            return true;

        if (pid == Environment.ProcessId)
            return true;

        try
        {
            using var process = Process.GetProcessById(pid);
            return !process.HasExited;
        }
        catch (ArgumentException)
        {
            return false; // No process with that id: the file outlived whoever made it.
        }
        catch (Exception ex)
        {
            Logger.Debug($"[ScratchFileCopy] Could not tell whether {pid} is still running: {ex.Message}; keeping {file}.");
            return true;
        }
    }

    /// <summary>
    /// The scratch directory, or false when there cannot be one - a read-only or full temp
    /// filesystem, or one this process may not write to.
    /// </summary>
    private static bool TryGetScratchDir(out string dir)
    {
        try
        {
            dir = LyraIO.GetScratchDir();
            return true;
        }
        catch (Exception ex)
        {
            Logger.Debug($"[ScratchFileCopy] No scratch directory available: {ex.Message}; decoding from the source path instead.");
            dir = string.Empty;
            return false;
        }
    }

    private static bool TryReserve(long sizeBytes)
    {
        while (true)
        {
            var current = Interlocked.Read(ref _reservedBytes);
            var next = current + sizeBytes;

            if (next > MaxConcurrentReservationBytes)
            {
                Logger.Info(
                    $"[ScratchFileCopy] Skipping local scratch copy: {next / (1024 * 1024)} MB would exceed the " +
                    $"{MaxConcurrentReservationBytes / (1024 * 1024)} MB concurrent budget; decoding from the source path instead."
                );
                return false;
            }

            if (Interlocked.CompareExchange(ref _reservedBytes, next, current) == current)
                return true;
        }
    }

    private static void Release(long sizeBytes) => Interlocked.Add(ref _reservedBytes, -sizeBytes);

    private static bool HasRoom(string dir, long sizeBytes)
    {
        try
        {
            var drive = new DriveInfo(dir);
            if (drive.AvailableFreeSpace >= sizeBytes + SafetyMarginBytes)
                return true;

            Logger.Info(
                $"[ScratchFileCopy] Skipping local scratch copy: {drive.AvailableFreeSpace / (1024 * 1024)} MB free at " +
                $"{dir} isn't enough for a {sizeBytes / (1024 * 1024)} MB file plus headroom; decoding from the source path instead."
            );
            return false;
        }
        catch (Exception ex)
        {
            Logger.Debug(
                $"[ScratchFileCopy] Could not check free space at {dir}: {ex.Message}; decoding from the source path instead.");
            return false;
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch
        {
            // Best effort: a leftover scratch file is swept on the next startup.
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;

        TryDelete(Path);
        Release(_reservedForThis);
    }
}
