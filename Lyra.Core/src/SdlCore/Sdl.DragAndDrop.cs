using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using Lyra.Common;
using Lyra.FileLoader;
using static SDL3.SDL;

namespace Lyra.SdlCore;

public partial class SdlCore
{
    private sealed class DropSession : IDisposable
    {
        public readonly CancellationTokenSource Cts = new();

        public volatile bool InProgress;
        public volatile bool AcceptFiles = true;

        public Task? Worker;

        public void Dispose() => Cts.Dispose();
    }

    private DropSession? _drop;
    
    // Drop queue fed by SDL thread
    private readonly ConcurrentQueue<string> _dropQueue = new();

    private const int BatchSize = 256;
    private const int DropStatsUpdateIntervalMs = 250;

    private void OnDropBegin()
    {
        Logger.Info("[DragAndDrop] File drop started.");

        // Cancel and replace any existing session (defensive).
        if (_drop is not null)
        {
            Logger.Info("[DragAndDrop] Cancelling previous drop session.");
            CancelDropInternal(_drop, resetProgress: true, markAborted: false);
            _drop.Dispose();
            _drop = null;
        }
        else
        {
            ClearDropQueue();
            _dropStats.Reset();
        }

        _dropStats.Start();

        var session = new DropSession
        {
            InProgress = true,
            AcceptFiles = true
        };

        _drop = session;
        session.Worker = Task.Run(() => ProcessDropAsync(session));
    }

    private void OnDropFile(Event e)
    {
        var session = _drop;
        if (session is null || !session.AcceptFiles)
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

        var session = _drop;
        if (session is null)
            return;

        session.InProgress = false;
        session.AcceptFiles = false; // ignore any late DROPFILE spam after complete
    }

    private void CancelDrop()
    {
        if (!_dropStats.GetDropStatus().Active)
            return;

        var session = _drop;
        if (session is null)
            return;

        Logger.Info("[DragAndDrop] Cancelling drop.");
        CancelDropInternal(session, resetProgress: false, markAborted: true);
    }

    private void CancelDropInternal(DropSession session, bool resetProgress, bool markAborted)
    {
        session.AcceptFiles = false;
        session.InProgress = false;

        try
        {
            session.Cts.Cancel();
        }
        catch
        {
            // ignored
        }

        if (resetProgress)
            ClearDropQueue();

        if (markAborted)
            _dropStats.MarkAborted();

        if (resetProgress)
            _dropStats.Reset();
    }

    private void ClearDropQueue()
    {
        while (_dropQueue.TryDequeue(out _))
        {
        }
    }

    private async Task ProcessDropAsync(DropSession session)
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
                    if (session.Cts.IsCancellationRequested)
                        break;

                    if (!session.InProgress && _dropQueue.IsEmpty)
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
                    cancellationToken: session.Cts.Token,
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
                    Interlocked.Add(ref pendingSupported, missingSupported);

                FlushStatsIfDue(force: true);

                // Apply results back on the main thread
                DispatchToMain(() =>
                {
                    DirectoryNavigator.ApplyCollection(files, dropContext, singleDirectory, topDirectory);
                    LoadImage();
                    RaiseWindow(_window);
                }, requireWarm: true);

                batch.Clear();
                await Task.Yield();

                // If cancellation was requested, do one last processed batch (above) and exit.
                if (session.Cts.IsCancellationRequested)
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
            FlushStatsIfDue(force: true);
            _dropStats.Finish();

            // Clear the session only if it's still the current one.
            if (ReferenceEquals(_drop, session))
                _drop = null;

            session.Dispose();
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