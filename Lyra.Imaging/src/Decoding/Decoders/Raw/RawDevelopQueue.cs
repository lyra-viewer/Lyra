using Lyra.Common;

namespace Lyra.Imaging.Decoding.Decoders.Raw;

/// <summary>
/// Runs LibRaw developments one at a time on a long-lived thread, the image on screen first.
/// Two at once oversubscribe the cores, and their OpenMP teams then queue on LibRaw's global
/// critical sections for tens of seconds.
/// </summary>
internal sealed class RawDevelopQueue : IDisposable
{
    public static RawDevelopQueue Shared { get; } = new();

    /// <summary>How often the idle thread looks again while work waits, should a wake-up be missed.</summary>
    private static readonly TimeSpan RecheckInterval = TimeSpan.FromMilliseconds(100);

    private sealed class Job(Func<CancellationToken, object?> work, Func<bool> foreground, Func<bool> finishing, CancellationToken ct)
    {
        public readonly Func<CancellationToken, object?> Work = work;
        public readonly Func<bool> Foreground = foreground;
        public readonly Func<bool> Finishing = finishing;
        public readonly CancellationToken Ct = ct;

        // Never completes on the develop thread, so no caller's continuation holds it up.
        public readonly TaskCompletionSource<object?> Done = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public CancellationTokenRegistration Registration;
        public bool Started;
        public bool CutShort;
    }

    private sealed class Announcement(RawDevelopQueue queue, Func<bool> foreground) : IDisposable
    {
        public readonly Func<bool> Foreground = foreground;

        public void Dispose() => queue.Withdraw(this);
    }

    private readonly object _gate = new(); // a monitor, for Wait and Pulse
    private readonly List<Job> _waiting = [];
    private readonly List<Announcement> _announced = [];
    private Job? _running;
    private CancellationTokenSource? _preemption; // the running job's
    private bool _stopping;

    public RawDevelopQueue()
    {
        var thread = new Thread(WorkerLoop)
        {
            IsBackground = true,
            Name = "RawDevelop"
        };

        thread.Start();
    }

    /// <summary>
    /// Runs <paramref name="work"/> on the develop thread. <paramref name="foreground"/> says, each
    /// time it is asked whether the work is for the image on screen. Work that cannot be cut
    /// short is waited out.
    /// </summary>
    public T Run<T>(Func<T> work, Func<bool> foreground, CancellationToken ct) => 
        Run(_ => work(), foreground, ct);

    /// <summary>
    /// As <see cref="Run{T}(Func{T}, Func{bool}, CancellationToken)"/>, for work that can be cut short:
    /// background work whose token fires should stop and throw on it, to run again from the start later.
    /// </summary>
    public T Run<T>(Func<CancellationToken, T> work, Func<bool> foreground, CancellationToken ct) =>
        Run(work, foreground, static () => false, ct);

    /// <summary>
    /// As <see cref="Run{T}(Func{CancellationToken, T}, Func{bool}, CancellationToken)"/>; while
    /// <paramref name="finishing"/> is true, the work is waited out instead of cut short.
    /// </summary>
    public T Run<T>(Func<CancellationToken, T> work, Func<bool> foreground, Func<bool> finishing, CancellationToken ct) =>
        RunAsync(work, foreground, finishing, ct).GetAwaiter().GetResult();

    /// <summary>
    /// As <see cref="Run{T}(Func{CancellationToken, T}, Func{bool}, Func{bool}, CancellationToken)"/>,
    /// without holding a thread while the work waits its turn. Continuations never run on the develop thread.
    /// </summary>
    public Task<T> RunAsync<T>(Func<CancellationToken, T> work, Func<bool> foreground, Func<bool> finishing, CancellationToken ct)
    {
        if (ct.IsCancellationRequested)
            return Task.FromCanceled<T>(ct);

        var job = new Job(preempt => work(preempt), foreground, finishing, ct);

        // Before the job is queued, so the worker never sees it without its registration.
        job.Registration = ct.UnsafeRegister(static state =>
        {
            var (queue, job) = ((RawDevelopQueue, Job))state!;
            queue.Withdraw(job);
        }, (this, job));

        lock (_gate)
        {
            if (_stopping)
            {
                job.Registration.Unregister();
                return Task.FromException<T>(new ObjectDisposedException(nameof(RawDevelopQueue)));
            }

            _waiting.Add(job);
            Preempt();
            Monitor.PulseAll(_gate);
        }

        return As<T>(job.Done.Task);
    }

    private static async Task<T> As<T>(Task<object?> task) => (T)(await task.ConfigureAwait(false))!;
    
    public IDisposable Announce(Func<bool> foreground)
    {
        var announcement = new Announcement(this, foreground);

        lock (_gate)
        {
            _announced.Add(announcement);
            Preempt();
        }

        return announcement;
    }

    /// <summary>Call when the image on screen changes, so waiting and running work is weighed again.</summary>
    public void Reprioritize()
    {
        lock (_gate)
        {
            Preempt();
            Monitor.PulseAll(_gate);
        }
    }

    /// <summary>Stops the thread once the work already queued is done.</summary>
    public void Dispose()
    {
        lock (_gate)
        {
            _stopping = true;
            Monitor.PulseAll(_gate);
        }
    }

    /// <summary>Drops work whose load was canceled before it started; work under way is waited out.</summary>
    private void Withdraw(Job job)
    {
        lock (_gate)
        {
            if (job.Started || !_waiting.Remove(job))
                return;

            job.Done.TrySetCanceled(job.Ct);
            Monitor.PulseAll(_gate);
        }
    }

    private void Withdraw(Announcement announcement)
    {
        lock (_gate)
        {
            if (_announced.Remove(announcement))
                Monitor.PulseAll(_gate);
        }
    }

    private void WorkerLoop()
    {
        while (true)
        {
            Job job;
            CancellationTokenSource preemption;

            lock (_gate)
            {
                while (!TryTake(out job))
                {
                    if (_stopping && _waiting.Count == 0)
                        return;

                    if (_waiting.Count > 0)
                        Monitor.Wait(_gate, RecheckInterval);
                    else
                        Monitor.Wait(_gate);
                }

                job.Started = true;
                _running = job;
                _preemption = preemption = new CancellationTokenSource();
                Preempt();
            }

            var preempted = false;
            object? result = null;
            Exception? error = null;

            try
            {
                result = job.Work(preemption.Token);
            }
            catch (OperationCanceledException e) when (e.CancellationToken == preemption.Token && preemption.IsCancellationRequested)
            {
                preempted = true;
            }
            catch (Exception e)
            {
                error = e;
            }

            lock (_gate)
            {
                _running = null;
                _preemption = null;
                preemption.Dispose();

                if (preempted && !job.Ct.IsCancellationRequested)
                {
                    job.Started = false;
                    job.CutShort = true;
                    _waiting.Add(job);
                }
                else
                {
                    job.Registration.Unregister();

                    if (preempted)
                        job.Done.TrySetCanceled(job.Ct);
                    else if (error is not null)
                        job.Done.TrySetException(error);
                    else
                        job.Done.TrySetResult(result);
                }

                Monitor.PulseAll(_gate);
            }
        }
    }
    
    private bool TryTake(out Job job)
    {
        _waiting.RemoveAll(Dropped);

        var found = _waiting.Find(j => Ask(j.Foreground));

        if (found is null && _waiting.Count > 0)
        {
            if (_stopping)
                found = _waiting[0];
            else if (!ForegroundBusy())
                found = _waiting.Find(j => !j.CutShort) ?? _waiting[0];
        }

        job = found!;
        if (found is null)
            return false;

        _waiting.Remove(found);
        return true;
    }
    
    private void Preempt()
    {
        if (_stopping || _running is not { } running || _preemption is not { IsCancellationRequested: false } preemption
            || Ask(running.Foreground) || !ForegroundBusy() || Ask(running.Finishing))
            return;

        try
        {
            preemption.Cancel();
        }
        catch (AggregateException e)
        {
            Logger.Warning(typeof(RawDevelopQueue), $"Cutting short a background develop failed: {e.InnerException?.Message}");
        }
    }

    private static bool Dropped(Job job)
    {
        if (!job.Ct.IsCancellationRequested)
            return false;

        job.Registration.Unregister();
        job.Done.TrySetCanceled(job.Ct);
        return true;
    }

    private bool ForegroundBusy() => _waiting.Exists(j => Ask(j.Foreground)) || _announced.Exists(a => Ask(a.Foreground));

    /// <summary>Asked under the lock, on the worker thread among others, which must not die of it.</summary>
    private static bool Ask(Func<bool> question)
    {
        try
        {
            return question();
        }
        catch
        {
            return false;
        }
    }
}