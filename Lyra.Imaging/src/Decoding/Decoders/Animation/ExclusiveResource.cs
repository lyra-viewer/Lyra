namespace Lyra.Imaging.Decoding.Decoders.Animation;

/// <summary>
/// Resources one piece of work uses at a time, which a dispose from another thread must not pull
/// from under it: a dispose during the work defers <see cref="Release"/> until the work ends, and
/// work asked for after a dispose is refused as a cancellation.
/// </summary>
internal abstract class ExclusiveResource : IDisposable
{
    private readonly Lock _work = new();
    private readonly Lock _state = new();

    private bool _busy;
    private bool _disposed;

    protected T Exclusive<T>(Func<T> work)
    {
        lock (_work)
        {
            lock (_state)
            {
                if (_disposed)
                    throw new OperationCanceledException("The frame set was closed.");

                _busy = true;
            }

            try
            {
                return work();
            }
            finally
            {
                lock (_state)
                {
                    _busy = false;
                    if (_disposed)
                        Release();
                }
            }
        }
    }

    protected abstract void Release();

    public void Dispose()
    {
        lock (_state)
        {
            _disposed = true;
            if (!_busy)
                Release();
        }
    }
}