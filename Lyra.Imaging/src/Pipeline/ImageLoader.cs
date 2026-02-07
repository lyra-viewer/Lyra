using System.Collections.Concurrent;
using Lyra.Common;
using Lyra.Common.SystemExtensions;
using Lyra.Imaging.Content;

namespace Lyra.Imaging.Pipeline;

internal class ImageLoader
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

    private readonly ConcurrentDictionary<string, Lazy<ImageJob>> _images = new();
    private readonly TaskFactory _preloadTaskFactory = new(new PreloadTaskScheduler(2));
    private volatile Composite? _currentImage;

    #endregion

    #region Public Methods

    /// <summary>Returns a stable Composite immediately. Starts async load if needed (non-blocking).</summary>
    public Composite GetImage(string path)
    {
        var lazy = _images.GetOrAdd(
            path,
            p => CreateLazyJob(p, isPreload: false));

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

    /// <summary>Preload adjacent images in the background with bounded concurrency.</summary>
    public void PreloadAdjacent(string[] paths)
    {
        foreach (var path in paths)
            TryPreload(path);
    }

    /// <summary>Remove everything not in 'keep' array. Cancels in-flight work and disposes completed images not current.</summary>
    public void Cleanup(string[] keep)
    {
        var keepSet = new HashSet<string>(keep);
        RemoveMatching(key => !keepSet.Contains(key), "Cleanup:");
    }

    /// <summary>Disposes everything and cancels in-flight jobs.</summary>
    public void DisposeAll()
    {
        RemoveMatching(_ => true, "Disposing:");
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

    private void TryPreload(string path)
    {
        if (ImageFormat.IsPreloadDisabled(Path.GetExtension(path)))
            return;

        var lazy = _images.GetOrAdd(
            path,
            p => CreateLazyJob(p, isPreload: true));

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

    private async Task LoadImageAsync(Composite composite, CancellationToken ct)
    {
        var extension = composite.FileInfo.Extension;
        var fileSize = composite.FileInfo.Length;

        composite.ImageFormatType = ImageFormat.GetImageFormat(extension);
        composite.LoadTimeEstimated = LoadTimeEstimator.EstimateLoadTime(extension, fileSize);

        composite.Completed += OnCompleted;

        try
        {
            var decoder = DecoderManager.GetDecoder(composite.ImageFormatType);
            composite.State = CompositeState.Loading;
            composite.BeginLoadTiming();

            ct.ThrowIfCancellationRequested();

            await decoder.DecodeAsync(composite, ct).ConfigureAwait(false);

            ct.ThrowIfCancellationRequested();

            if (composite.IsEmpty)
            {
                composite.State = CompositeState.Failed;
                return;
            }

            composite.SignalReady();

            // Promote to Complete if:
            // - decoder finished everything synchronously (still Loading), or
            // - content is not a streaming RasterLarge (no tiles), or tiles are already fully ready.
            if (composite.State == CompositeState.Loading)
                composite.SignalComplete();
            else if (composite.State == CompositeState.Ready)
            {
                if (composite.Content is not RasterLargeContent large || !large.HasTiles || (large.TilesTotal is int total && large.TilesReady >= total))
                    composite.SignalComplete();
            }
        }
        catch (OperationCanceledException)
        {
            composite.State = CompositeState.Cancelled;
        }
        catch (Exception ex)
        {
            Logger.Error($"[ImageLoader] Failed to load image {composite.FileInfo.FullName}: {ex}");
            composite.State = CompositeState.Failed;
        }

        return;

        void OnCompleted(Composite c)
        {
            if (c.LoadTimeComplete is double time)
                LoadTimeEstimator.RecordLoadTime(extension, fileSize, time);

            c.Completed -= OnCompleted;
        }
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

            if (!removed.Task.IsCompleted)
            {
                removed.Cts.CancelSilently();
                AttachCleanupContinuation(removed, key, context);
            }
            else
            {
                DisposeJobNow(removed, key, context);
            }
        }
    }

    private void DisposeJobNow(ImageJob job, string key, string context)
    {
        LogTerminalState(job.Task, job.Composite, key, context);
        DisposeIfNotCurrent(job.Composite);
        job.Cts.CancelAndDisposeSilently();
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
            TaskScheduler.Default);
    }

    private static void OnJobFinished(Task task, object? stateObj)
    {
        var state = (DisposeContinuationState)stateObj!;
        LogTerminalState(task, state.Composite, state.Key, state.Context);

        state.Loader.DisposeIfNotCurrent(state.Composite);
        state.Cts.CancelAndDisposeSilently();
    }

    private static void LogTerminalState(Task task, Composite composite, string key, string context)
    {
        var state = composite.State;

        if (state == CompositeState.Cancelled)
        {
            Logger.Debug($"[ImageLoader] {context} cancelled decode: {key}");
            return;
        }

        if (task.IsFaulted)
        {
            Logger.Warning($"[ImageLoader] {context} failed decode: {key}: {task.Exception}");
            return;
        }

        if (state != CompositeState.Complete)
        {
            Logger.Warning($"[ImageLoader] {context} not complete: {key} (state={state}).");
        }
    }

    #endregion
}