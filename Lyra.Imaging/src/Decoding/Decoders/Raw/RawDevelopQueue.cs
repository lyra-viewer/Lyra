using System.Runtime.ExceptionServices;

namespace Lyra.Imaging.Decoding.Decoders.Raw;

/// <summary>Runs LibRaw developments on two long-lived threads, the image on screen first.</summary>
internal sealed class RawDevelopQueue : IDisposable
{
    public static RawDevelopQueue Shared { get; } = new();

    private sealed class Job(Func<object?> work, CancellationToken ct)
    {
        public readonly Func<object?> Work = work;
        public readonly CancellationToken Ct = ct;
        public readonly ManualResetEventSlim Done = new(false);
        public object? Result;
        public ExceptionDispatchInfo? Error;
        public bool Started;
        public bool Abandoned;
    }

    private readonly object _gate = new(); // a monitor, for Wait and Pulse
    private readonly Queue<Job> _foreground = new();
    private readonly Queue<Job> _preload = new();
    private bool _stopping;

    public RawDevelopQueue()
    {
        Start("RawDevelop-0", takesPreloads: true);
        Start("RawDevelop-1", takesPreloads: false);
    }
    
    public T Run<T>(Func<T> work, bool preload, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        var job = new Job(() => work(), ct);

        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_stopping, this);

            (preload ? _preload : _foreground).Enqueue(job);
            Monitor.PulseAll(_gate);
        }

        try
        {
            WaitHandle.WaitAny([job.Done.WaitHandle, ct.WaitHandle]);

            if (!job.Done.IsSet)
            {
                lock (_gate)
                {
                    if (!job.Started)
                    {
                        job.Abandoned = true;
                        ct.ThrowIfCancellationRequested();
                    }
                }

                job.Done.Wait(CancellationToken.None);
            }

            job.Error?.Throw();
            return (T)job.Result!;
        }
        finally
        {
            lock (_gate)
            {
                if (job.Abandoned || job.Done.IsSet)
                    job.Done.Dispose();
                else
                    job.Abandoned = true; // the worker disposes it when it finishes
            }
        }
    }

    /// <summary>Stops the threads once the work already queued is done.</summary>
    public void Dispose()
    {
        lock (_gate)
        {
            _stopping = true;
            Monitor.PulseAll(_gate);
        }
    }

    private void Start(string name, bool takesPreloads)
    {
        var thread = new Thread(() => WorkerLoop(takesPreloads))
        {
            IsBackground = true,
            Name = name
        };

        thread.Start();
    }

    private void WorkerLoop(bool takesPreloads)
    {
        while (true)
        {
            Job job;

            lock (_gate)
            {
                while (!TryTake(takesPreloads, out job))
                {
                    if (_stopping)
                        return;

                    Monitor.Wait(_gate);
                }

                job.Started = true;
            }

            try
            {
                job.Result = job.Work();
            }
            catch (Exception e)
            {
                job.Error = ExceptionDispatchInfo.Capture(e);
            }

            lock (_gate)
            {
                if (job.Abandoned)
                    job.Done.Dispose();
                else
                    job.Done.Set();
            }
        }
    }

    /// <summary>The next job worth starting; abandoned ones are dropped on the way.</summary>
    private bool TryTake(bool takesPreloads, out Job job)
    {
        while (_foreground.TryDequeue(out job!) || (takesPreloads && _preload.TryDequeue(out job!)))
        {
            if (!job.Abandoned && !job.Ct.IsCancellationRequested)
                return true;

            job.Abandoned = true;
        }

        job = null!;
        return false;
    }
}