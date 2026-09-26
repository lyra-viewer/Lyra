using System.Collections.Concurrent;
using Lyra.Common;
using Lyra.Common.Estimation;
using Lyra.Common.SystemExtensions;
using Lyra.Imaging.Content;
using Lyra.Imaging.Content.Tiling;

namespace Lyra.Imaging.Loading;

internal class ImageLoader : IDisposable
{
    #region Nested Types & Delegates

    private sealed class ImageJob
    {
        public required Composite Composite { get; init; }
        public required Task Task { get; init; }
        public required CancellationTokenSource Cts { get; init; }
    }

    private sealed class DisposeContinuationState
    {
        public required Composite Composite { get; init; }
        public required ImageLoader Loader { get; init; }
        public required CancellationTokenSource Cts { get; init; }
        public required string Key { get; init; }
        public required string Context { get; init; }
    }

    private static readonly Action<Task, object?> ActionOnJobFinished = OnJobFinished;

    #endregion

    #region Fields

    /// <summary>
    /// How much decoded pixel data the cache may hold before neighbors are evicted.
    /// </summary>
    private static readonly long CacheByteBudget = ComputeCacheBudget(GC.GetGCMemoryInfo().TotalAvailableMemoryBytes);

    /// <summary>What the cache may hold on this machine.</summary>
    internal static long CacheBudgetBytes => CacheByteBudget;

    private const long MinimumCacheBudget = 1536L * 1024 * 1024;
    private const long MaximumCacheBudget = 8L * 1024 * 1024 * 1024;

    /// <summary>The budget for a machine with <paramref name="availableBytes"/> to work with.</summary>
    internal static long ComputeCacheBudget(long availableBytes)
        => availableBytes <= 0
            ? MinimumCacheBudget
            : Math.Clamp(availableBytes / 8, MinimumCacheBudget, MaximumCacheBudget);

    private readonly ConcurrentDictionary<string, Lazy<ImageJob>> _images = new();

    /// <summary>
    /// The most recent keep window, so the budget can be re-enforced when decode completes.
    /// Checking only at Cleanup time is not enough: that runs before the new image is decoded, and
    /// the decodes it triggers land afterward, so the peak falls between two checks.
    /// </summary>
    private volatile string[] _lastKeepWindow = [];
    private readonly PreloadTaskScheduler _preloadScheduler = new(2);
    private readonly TaskFactory _preloadTaskFactory;
    private volatile Composite? _currentImage;

    public ImageLoader()
    {
        _preloadTaskFactory = new TaskFactory(_preloadScheduler);

        Logger.Info(typeof(ImageLoader),
            $"Decoded-image cache budget: {CacheByteBudget / 1024 / 1024} MB " +
            $"(of {GC.GetGCMemoryInfo().TotalAvailableMemoryBytes / 1024 / 1024} MB available)."
        );
    }

    #endregion

    #region Public Methods

    /// <summary>Returns a stable Composite immediately. Starts async load if needed (non-blocking).</summary>
    public Composite GetImage(string path)
    {
        // Cleared first so the load started below never yields to the outgoing image.
        _currentImage = null;

        if (FailedTransiently(path))
            RemoveMatching(key => key == path, "Retrying:");

        var lazy = _images.GetOrAdd(path, p => CreateLazyJob(p, isPreload: false));

        ImageJob job;
        try
        {
            job = lazy.Value; // StartJob executes only once for the stored Lazy.
        }
        catch
        {
            // If StartJob throws synchronously, Lazy caches the exception; remove so callers can retry.
            _images.TryRemove(new KeyValuePair<string, Lazy<ImageJob>>(path, lazy));
            throw;
        }

        _currentImage = job.Composite;
        return job.Composite;
    }

    private bool FailedTransiently(string path) =>
        _images.TryGetValue(path, out var lazy) && lazy.IsValueCreated && IsWorthRetrying(lazy.Value.Composite);

    internal static bool IsWorthRetrying(Composite composite) =>
        composite is { State: CompositeState.Failed, Failure.IsTransient: true };

    public bool IsLoading(string path) =>
        _images.TryGetValue(path, out var lazy)
        && lazy.IsValueCreated
        && lazy.Value.Composite.State is CompositeState.Pending or CompositeState.Loading;

    /// <summary>Preload adjacent images in the background with bounded concurrency.</summary>
    public void PreloadAdjacent(string[] paths)
    {
        foreach (var path in paths)
            TryPreload(path);
    }

    /// <summary>
    /// Drops cached images until the resident set fits the budget, most expensive to keep first.
    /// The current image is never evicted, however large it is - refusing to show what the user
    /// asked for would be worse than the memory.
    /// </summary>
    private void EnforceByteBudget(string[] keep)
    {
        var resident = ResidentBytes();
        if (resident <= CacheByteBudget)
            return;

        var current = _currentImage;
        var centre = Centre(keep, current?.FileInfo.FullName);

        var candidates = new List<EvictionCandidate>();
        for (var index = 0; index < keep.Length; index++)
        {
            var path = keep[index];

            if (!_images.TryGetValue(path, out var lazy) || !lazy.IsValueCreated)
                continue;

            if (ReferenceEquals(lazy.Value.Composite, current))
                continue;

            var bytes = SafeByteSize(lazy.Value.Composite);
            if (bytes <= 0)
                continue;

            candidates.Add(new EvictionCandidate(path, Math.Abs(index - centre), bytes));
        }

        foreach (var candidate in EvictionOrder(candidates))
        {
            if (resident <= CacheByteBudget)
                return;

            RemoveMatching(key => PathComparer.Equals(key, candidate.Path), "Budget:");
            resident -= candidate.Bytes;

            Logger.Debug(typeof(ImageLoader),
                $"Evicted {Path.GetFileName(candidate.Path)} " +
                $"({candidate.Bytes / 1024 / 1024} MB, {candidate.Distance} away) to stay in budget."
            );
        }
    }

    /// <summary>
    /// Where in the keep window the current image sits - the point distances are measured from.
    /// </summary>
    internal static int Centre(string[] keep, string? currentPath)
    {
        if (currentPath is null)
            return keep.Length / 2;

        var index = Array.FindIndex(keep, path => PathComparer.Equals(path, currentPath));

        return index >= 0 ? index : keep.Length / 2;
    }

    /// <summary>One cached image the budget could reclaim, and what reclaiming it would cost.</summary>
    internal readonly record struct EvictionCandidate(string Path, int Distance, long Bytes);

    /// <summary>
    /// Orders candidates by what they cost to keep: big and far goes first, small and near last.
    /// </summary>
    internal static IReadOnlyList<EvictionCandidate> EvictionOrder(IReadOnlyList<EvictionCandidate> candidates)
        => candidates
            .OrderByDescending(c => c.Bytes * (c.Distance + 1L))
            .ThenByDescending(c => c.Distance)
            .ToList();

    /// <summary>
    /// Re-checks the budget against the window last navigated to. Called when a decode finishes,
    /// which is the moment residency actually grows.
    /// </summary>
    private void EnforceByteBudgetAfterDecode() => EnforceByteBudget(_lastKeepWindow);

    /// <summary>Decoded bytes currently held by the cache. Exposed for profiling.</summary>
    public long ResidentBytes()
    {
        var total = 0L;

        foreach (var lazy in _images.Values)
        {
            if (!lazy.IsValueCreated)
                continue;

            total += SafeByteSize(lazy.Value.Composite);
        }

        return total;
    }

    /// <summary>
    /// A composite's footprint, tolerating one being disposed on another thread mid-measurement -
    /// this runs while decodes and evictions are in flight, and a torn read here must not take
    /// down navigation.
    /// </summary>
    private static long SafeByteSize(Composite composite)
    {
        try
        {
            return composite.Content?.ByteSize ?? 0;
        }
        catch (Exception)
        {
            return 0;
        }
    }

    /// <summary>Remove everything not in 'keep' array. Cancels in-flight work and disposes completed images not current.</summary>
    public void Cleanup(string[] keep)
    {
        var keepSet = new HashSet<string>(keep);
        RemoveMatching(key => !keepSet.Contains(key), "Cleanup:");

        // The window says which neighbors are worth keeping; the budget says how many of them
        // actually fit.
        _lastKeepWindow = keep;
        EnforceByteBudget(keep);
    }

    public void Purge(string path)
    {
        RemoveMatching(key => PathComparer.Equals(key, path), "Purge:");
    }

    public void Dispose()
    {
        RemoveMatching(_ => true, "Disposing:");
        _preloadScheduler.Dispose();
    }

    #endregion

    #region Task Pipeline (start, preload, decode)

    private ImageJob StartJob(string path, bool isPreload)
    {
        var composite = new Composite(new FileInfo(path));
        var cts = new CancellationTokenSource();

        var task = isPreload
            ? _preloadTaskFactory.StartNew(() => LoadImageAsync(composite, cts.Token), CancellationToken.None).Unwrap()
            : Task.Run(() => LoadImageAsync(composite, cts.Token)); // no token overload

        return new ImageJob
        {
            Composite = composite,
            Task = task,
            Cts = cts
        };
    }

    private Lazy<ImageJob> CreateLazyJob(string path, bool isPreload) =>
        new(() => StartJob(path, isPreload), LazyThreadSafetyMode.ExecutionAndPublication);

    private const double PreloadTransferCeilingMs = 10_000;

    /// <summary>Applies until the source's speed has been measured.</summary>
    private const long UnmeasuredPreloadCeilingBytes = 256L * 1024 * 1024;

    internal static bool WorthPreloading(long fileBytes, TransferEstimate? estimate, out double expectedMs)
    {
        if (estimate is { } source)
        {
            expectedMs = source.MsFor(fileBytes);
            return expectedMs <= PreloadTransferCeilingMs;
        }

        expectedMs = 0;
        return fileBytes <= UnmeasuredPreloadCeilingBytes;
    }

    private void TryPreload(string path)
    {
        if (ImageFormat.IsPreloadDisabled(Path.GetExtension(path)))
            return;

        if (!_images.ContainsKey(path) && !WorthPreloadingNow(path))
            return;

        var lazy = _images.GetOrAdd(path, p => CreateLazyJob(p, isPreload: true));

        // Touching Value starts the preload (exactly once for the stored Lazy).
        try
        {
            _ = lazy.Value;
        }
        catch
        {
            // Remove poison entry so future attempts can retry.
            _images.TryRemove(new KeyValuePair<string, Lazy<ImageJob>>(path, lazy));
        }
    }

    private readonly ConcurrentDictionary<string, byte> _preloadSkipsReported = new(StringComparer.OrdinalIgnoreCase);

    private bool WorthPreloadingNow(string path)
    {
        long fileBytes;
        try
        {
            fileBytes = new FileInfo(path).Length;
        }
        catch (Exception ex)
        {
            Logger.Debug(typeof(ImageLoader), $"Could not size {path} for preload: {ex.Message}");
            return true;
        }

        var estimate = SourceThroughputEstimator.EstimateTransfer(path);
        if (WorthPreloading(fileBytes, estimate, out var expectedMs))
            return true;

        if (!_preloadSkipsReported.TryAdd(path, 0))
            return false;

        Logger.Debug(typeof(ImageLoader), estimate is null
            ? $"[ImageLoader] Not preloading {Path.GetFileName(path)}: {fileBytes / (1024 * 1024)} MB from a source whose speed is not known yet. It loads when opened."
            : $"[ImageLoader] Not preloading {Path.GetFileName(path)}: {fileBytes / (1024 * 1024)} MB would take about {expectedMs / 1000:F0}s to fetch. It loads when opened."
        );

        return false;
    }

    /// <summary>Ordinary images load within this, so browsing them never makes other loads wait.</summary>
    private const double YieldAfterMs = 500;

    private bool ForegroundBusy(Composite load) => ShouldYield(_currentImage, load);

    /// <summary>
    /// Only an image already <see cref="CompositeState.Loading"/> is yielded to: one still pending
    /// may be queued behind the very workers that would wait for it.
    /// </summary>
    internal static bool ShouldYield(Composite? onScreen, Composite load, double yieldAfterMs = YieldAfterMs) =>
        onScreen is { } current
        && !ReferenceEquals(current, load)
        && current.State == CompositeState.Loading
        && current.Timing.ElapsedMs >= yieldAfterMs;

    private static string DecodedBy(Composite composite) => composite.DecoderName is { } decoder ? $" ({decoder})" : string.Empty;

    private static void Fail(Composite composite, LoadFailure failure)
    {
        if (failure.IsExpected)
            Logger.Warning(typeof(ImageLoader), $"{failure.Message}: {composite.FileInfo.FullName}{DecodedBy(composite)}. Detail: {failure.Detail}");

        composite.Fail(failure);
    }

    private async Task LoadImageAsync(Composite composite, CancellationToken ct)
    {
        var extension = composite.FileInfo.Extension;
        var fileSize = composite.FileSizeBytes;

        composite.ImageFormatType = ImageFormat.GetImageFormat(extension);

        ApplyEstimate(composite, extension, fileSize, pixels: null);
        composite.Timing.TransferBytesTotal = fileSize ?? 0;

        composite.Completed += OnCompleted;
        composite.PixelCountReported += OnPixelCountReported;

        using var yielding = ForegroundYield.EnterLoad(composite.FileInfo.Name, () => ForegroundBusy(composite), composite.Timing.AddPause);

        try
        {
            var decoder = DecoderManager.GetDecoder(composite.ImageFormatType);

            ForegroundYield.WaitForForeground(ct);

            composite.State = CompositeState.Loading;
            composite.BeginLoadTiming();

            ct.ThrowIfCancellationRequested();

            await decoder.DecodeAsync(composite, ct).ConfigureAwait(false);

            ct.ThrowIfCancellationRequested();

            if (composite.IsEmpty)
            {
                Fail(composite, LoadFailure.NothingDecoded);
                return;
            }

            var largeContent = composite.Content as RasterLargeContent;
            if (largeContent is not null)
                largeContent.TilesProgressChanged += _ => composite.SignalProgress();
            
            if (composite.Content is VariantRasterContent variants)
            {
                variants.VariantReady += _ => composite.SignalProgress();
                variants.VariantFailed += _ => composite.SignalProgress();
            }
            
            if (largeContent?.TileSource is LazyTileSource lazyTiles)
                lazyTiles.TileReady += _ => composite.SignalProgress();

            composite.SignalReady();
            
            EnforceByteBudgetAfterDecode();

            // Promote to Complete if:
            // - decoder finished everything synchronously (still Loading), or
            // - content is not a streaming RasterLarge (no tiles), or tiles are already fully ready.
            if (composite.State == CompositeState.Loading)
                composite.SignalComplete();
            else if (composite.State == CompositeState.Ready)
            {
                if (largeContent is null
                    || !largeContent.HasTiles
                    || (largeContent.TilesTotal is int total && largeContent.TilesReady >= total))
                {
                    composite.SignalComplete();
                }
            }
        }
        catch (OperationCanceledException)
        {
            composite.State = CompositeState.Cancelled;
        }
        catch (Exception ex)
        {
            var failure = LoadFailure.From(ex, composite.FileInfo.FullName);

            if (!failure.IsExpected)
                Logger.Error(typeof(ImageLoader), $"Failed to load image {composite.FileInfo.FullName}{DecodedBy(composite)}: {ex}");

            Fail(composite, failure);
        }

        return;
        
        void OnPixelCountReported(Composite c)
        {
            ApplyEstimate(c, extension, fileSize, c.PixelCount);
            c.SignalProgress();
        }

        void OnCompleted(Composite c)
        {
            if (fileSize is { } bytes && c.Timing.DecodeMs is { } decodeMs)
                DecodeTimeEstimator.RecordDecodeTime(extension, bytes, PixelsOf(c), decodeMs);

            if (c.Timing.TransferMs is { } transfer)
                SourceThroughputEstimator.RecordTransfer(c.FileInfo.FullName, c.Timing.TransferBytesRead, transfer);

            c.PixelCountReported -= OnPixelCountReported;
            c.Completed -= OnCompleted;
        }
    }
    
    private static void ApplyEstimate(Composite composite, string extension, long? fileSize, long? pixels)
    {
        var estimate = fileSize is { } bytes
            ? DecodeTimeEstimator.EstimateDecodeTime(extension, bytes, pixels)
            : LoadEstimate.None;

        composite.Timing.DecodeEstimateMs = estimate.Ms;
    }

    private static long? PixelsOf(Composite composite)
    {
        if (composite.PixelCount is { } reported)
            return reported;

        var width = (long)composite.LogicalWidth;
        var height = (long)composite.LogicalHeight;

        return width > 0 && height > 0 ? width * height : null;
    }

    #endregion

    #region Cleanup

    private void RemoveMatching(Func<string, bool> predicate, string context)
    {
        foreach (var (key, _) in _images.ToArray())
        {
            if (!predicate(key))
                continue;

            if (!_images.TryRemove(key, out var removedLazy))
                continue;

            if (!removedLazy.IsValueCreated)
                continue; // nothing started => nothing to cancel/cleanup

            var removed = removedLazy.Value;

            // Cancel before touching anything: stops both the decode task and any background
            // tile streaming as early as possible, before content is disposed out from under them.
            removed.Cts.CancelSilently();

            if (!removed.Task.IsCompleted)
            {
                AttachCleanupContinuation(removed, key, context);
            }
            else
            {
                FinishAndDispose(removed.Task, removed.Composite, removed.Cts, key, context);
            }
        }
    }

    /// <summary>
    /// Disposes a removed job's composite, deferring while background decode work (PSD tile
    /// streaming) is still draining. Cancellation has already been signaled; the background
    /// task observes it and signals completion in its finally, at which point disposal runs.
    /// </summary>
    private void FinishAndDispose(Task task, Composite composite, CancellationTokenSource cts, string key, string context)
    {
        var background = composite.BackgroundDecodeTask;
        if (!background.IsCompleted)
        {
            Logger.Debug(typeof(ImageLoader), $"{context} waiting for background decode before dispose: {key}");
            background.ContinueWith(
                _ =>
                {
                    LogTerminalState(task, composite, key, context);
                    DisposeIfNotCurrent(composite);
                    cts.CancelAndDisposeSilently();
                },
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
            return;
        }

        LogTerminalState(task, composite, key, context);
        DisposeIfNotCurrent(composite);
        cts.CancelAndDisposeSilently();
    }

    private void DisposeIfNotCurrent(Composite composite)
    {
        if (!ReferenceEquals(composite, _currentImage) && composite.State != CompositeState.Disposed)
        {
            try
            {
                composite.Dispose();
            }
            catch
            {
                /* ignore */
            }
        }
    }

    #endregion

    #region Continuation & Logging

    private void AttachCleanupContinuation(ImageJob job, string key, string context)
    {
        var state = new DisposeContinuationState
        {
            Composite = job.Composite,
            Loader = this,
            Cts = job.Cts,
            Key = key,
            Context = context
        };

        job.Task.ContinueWith(
            ActionOnJobFinished,
            state,
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default
        );
    }

    private static void OnJobFinished(Task task, object? stateObj)
    {
        // The decode task finished after cancellation; background tile streaming may still be
        // draining, so route through the same defer-aware disposal as the synchronous path.
        var state = (DisposeContinuationState)stateObj!;
        state.Loader.FinishAndDispose(task, state.Composite, state.Cts, state.Key, state.Context);
    }

    private static void LogTerminalState(Task task, Composite composite, string key, string context)
    {
        var state = composite.State;

        if (state == CompositeState.Cancelled)
        {
            Logger.Debug(typeof(ImageLoader), $"{context} cancelled decode: {key}");
            return;
        }

        if (task.IsFaulted)
        {
            Logger.Warning(typeof(ImageLoader), $"{context} failed decode: {key}: {task.Exception}");
            return;
        }

        if (state != CompositeState.Complete)
        {
            Logger.Warning(typeof(ImageLoader), $"{context} not complete: {key} (state={state}).");
        }
    }

    #endregion
}