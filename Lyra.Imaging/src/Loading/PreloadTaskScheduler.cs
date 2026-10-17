using System.Collections.Concurrent;
using Lyra.Common;

namespace Lyra.Imaging.Loading;

internal sealed class PreloadTaskScheduler : TaskScheduler, IDisposable
{
    private readonly BlockingCollection<Task> _tasks = new();

    /// <summary>Queued and not yet taken, by a worker or by <see cref="Promote"/>.</summary>
    private readonly ConcurrentDictionary<Task, byte> _waiting = new();

    private readonly List<Thread> _threads;
    private bool _disposed;

    public PreloadTaskScheduler(int maxDegreeOfParallelism)
    {
        _threads = new List<Thread>(maxDegreeOfParallelism);

        for (var i = 0; i < maxDegreeOfParallelism; i++)
        {
            var thread = new Thread(WorkerLoop)
            {
                IsBackground = true,
                Name = $"PreloadWorker-{i}",
                Priority = ThreadPriority.BelowNormal
            };

            _threads.Add(thread);
            thread.Start();
        }
    }

    private void WorkerLoop()
    {
        foreach (var task in _tasks.GetConsumingEnumerable())
        {
            if (_waiting.TryRemove(task, out _))
                TryExecuteTask(task);
        }
    }

    /// <summary>
    /// Runs a task still waiting here on the thread pool at once, ahead of the queue. False when
    /// it is not waiting here: already started, or never queued here.
    /// </summary>
    public bool Promote(Task? task)
    {
        if (task is null || !_waiting.TryRemove(task, out _))
            return false;

        ThreadPool.UnsafeQueueUserWorkItem(static state => state.Scheduler.TryExecuteTask(state.Task), new Promotion(this, task), preferLocal: false);
        return true;
    }

    private sealed record Promotion(PreloadTaskScheduler Scheduler, Task Task);

    protected override IEnumerable<Task> GetScheduledTasks() => _waiting.Keys.ToArray();

    protected override void QueueTask(Task task)
    {
        _waiting.TryAdd(task, 0);

        try
        {
            _tasks.Add(task);
        }
        catch
        {
            _waiting.TryRemove(task, out _);
            throw;
        }
    }

    protected override bool TryExecuteTaskInline(Task task, bool taskWasPreviouslyQueued) => false;

    private static readonly TimeSpan ShutdownJoinTimeout = TimeSpan.FromSeconds(2);

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;

        _tasks.CompleteAdding();

        var allJoined = true;

        foreach (var thread in _threads)
        {
            if (thread.Join(ShutdownJoinTimeout))
                continue;

            allJoined = false;
            Logger.Warning($"[PreloadTaskScheduler] {thread.Name} did not finish within {ShutdownJoinTimeout.TotalSeconds}s; abandoning it so shutdown can proceed.");
        }
        
        if (allJoined)
            _tasks.Dispose();
    }
}