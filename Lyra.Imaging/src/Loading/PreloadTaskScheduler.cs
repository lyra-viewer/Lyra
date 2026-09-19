using System.Collections.Concurrent;
using Lyra.Common;

namespace Lyra.Imaging.Loading;

internal sealed class PreloadTaskScheduler : TaskScheduler, IDisposable
{
    private readonly BlockingCollection<Task> _tasks = new();
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
            TryExecuteTask(task);
    }

    protected override IEnumerable<Task> GetScheduledTasks() => _tasks.ToArray();
    protected override void QueueTask(Task task) => _tasks.Add(task);
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