using System.Collections.Concurrent;

namespace Lyra.Imaging.Pipeline;

internal sealed class PreloadTaskScheduler : TaskScheduler
{
    private readonly BlockingCollection<Task> _tasks = new();
    private readonly List<Thread> _threads;

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
}