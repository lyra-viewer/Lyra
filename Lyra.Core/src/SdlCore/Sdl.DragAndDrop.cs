using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using Lyra.Common;
using Lyra.FileLoader;
using static SDL3.SDL;

namespace Lyra.SdlCore;

public partial class SdlCore
{
    // Drop queue fed by SDL thread
    private readonly ConcurrentQueue<string> _dropQueue = new();

    private CancellationTokenSource? _dropCts;
    private Task? _dropTask; // keep

    private volatile bool _dropInProgress;  // between DropBegin..DropComplete
    private volatile bool _dropIgnore;      // set when cancelling to ignore remaining DropFile spam

    private const int BatchSize = 256;
    private const int DropStatsUpdateIntervalMs = 250;

    private void OnDropBegin()
    {
        Logger.Info("[DragAndDrop] File drop started.");

        CancelDropInternal(resetProgress: true, markAborted: false);

        _dropStats.Start();

        _dropCts = new CancellationTokenSource();
        _dropIgnore = false;
        _dropInProgress = true;

        _dropTask = Task.Run(() => ProcessDropAsync(_dropCts.Token));
    }

    private void OnDropFile(Event e)
    {
        if (_dropIgnore)
            return;

        var droppedFilePtr = e.Drop.Data;
        var path = Marshal.PtrToStringUTF8(droppedFilePtr);
        if (string.IsNullOrWhiteSpace(path))
            return;

        _dropQueue.Enqueue(path);
        _dropStats.AddPaths(1);
    }

    private void OnDropComplete()
    {
        Logger.Info("[DragAndDrop] File drop completed.");
        _dropInProgress = false;
    }

    private void CancelDrop()
    {
        if (!_dropStats.GetDropStatus().Active)
            return;

        Logger.Info("[DragAndDrop] Cancelling drop.");
        CancelDropInternal(resetProgress: false, markAborted: true);
    }

    private void CancelDropInternal(bool resetProgress, bool markAborted)
    {
        _dropIgnore = true;
        _dropCts?.Cancel();

        if (resetProgress)
            while (_dropQueue.TryDequeue(out _))
            {
            }

        if (markAborted)
            _dropStats.MarkAborted();

        if (resetProgress)
            _dropStats.Reset();
    }

    private async Task ProcessDropAsync(CancellationToken ct)
    {
        // Debounce a bit so it's being processed in batches, not per path.
        // Not cancellable: on cancel flush/apply whatever is processed.
        await Task.Delay(25);

        var batch = new List<string>(BatchSize);

        // Throttle cross-thread atomic writes (and any UI polling work) by batching counts.
        long pendingFiles = 0;
        long pendingSupported = 0;
        var lastFlushMs = Environment.TickCount64;

        try
        {
            while (true)
            {
                while (_dropQueue.TryDequeue(out var p))
                    batch.Add(p);

                if (batch.Count == 0)
                {
                    // If cancelled and nothing to process, we're done.
                    if (ct.IsCancellationRequested)
                        break;

                    if (!_dropInProgress)
                        break;

                    await Task.Delay(10);
                    continue;
                }

                // Heavy work OFF SDL thread, cancellable
                long batchSupportedAdded = 0;
                var files = FilePathProcessor.ProcessImagePaths(
                    batch,
                    recurseSubdirs: null,
                    out var singleDirectory,
                    out var topDirectory,
                    out var dropContext,
                    cancellationToken: ct,
                    onFileEnumerated: () =>
                    {
                        Interlocked.Increment(ref pendingFiles);
                        FlushStatsIfDue();
                    },
                    onSupportedFileDiscovered: () =>
                    {
                        batchSupportedAdded++;
                        Interlocked.Increment(ref pendingSupported);
                        FlushStatsIfDue();
                    });

                // Safety: if any supported files were added without triggering the callback,
                // reconcile here (should normally be zero).
                var missingSupported = files.Count - batchSupportedAdded;
                if (missingSupported > 0)
                    System.Threading.Interlocked.Add(ref pendingSupported, missingSupported);

                FlushStatsIfDue(force: true);

                // Apply results back on the main thread
                DeferUntilWarm(() =>
                {
                    DirectoryNavigator.ApplyCollection(files, dropContext, singleDirectory, topDirectory);
                    LoadImage();
                    DeferUntilWarm(() => RaiseWindow(_window));
                });

                batch.Clear();
                await Task.Yield();

                // If cancellation was requested, do one last processed batch (above) and exit.
                if (ct.IsCancellationRequested)
                    break;
            }
        }
        catch (OperationCanceledException)
        {
            // expected
            Logger.Info("[DragAndDrop] Drop processing cancelled.");
        }
        catch (Exception ex)
        {
            Logger.Error($"[DragAndDrop] Drop processing failed: {ex}");
        }
        finally
        {
            _dropInProgress = false;
            FlushStatsIfDue(force: true);
            _dropStats.Finish();
        }

        return;

        void FlushStatsIfDue(bool force = false)
        {
            var nowMs = Environment.TickCount64;
            if (!force && (nowMs - lastFlushMs) < DropStatsUpdateIntervalMs)
                return;

            lastFlushMs = nowMs;

            var filesToAdd = Interlocked.Exchange(ref pendingFiles, 0);
            if (filesToAdd > 0)
                _dropStats.AddFiles(filesToAdd);

            var supportedToAdd = Interlocked.Exchange(ref pendingSupported, 0);
            if (supportedToAdd > 0)
                _dropStats.AddSupported(supportedToAdd);
        }
    }
}