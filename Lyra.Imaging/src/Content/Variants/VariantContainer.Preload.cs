namespace Lyra.Imaging.Content.Variants;

/// <summary>
/// Preloading: the neighbors of the selection in <see cref="StepOrder"/> decoded ahead of being
/// asked for, one at a time, only within the budget, and only once the container is on screen.
/// </summary>
public sealed partial class VariantContainer
{
    private CancellationTokenSource? _preload;
    private int _preloadIndex = -1;
    private bool _preloadStarted;

    public bool PreloadsNeighbours { get; init; }

    /// <summary>
    /// Starts decoding neighbors ahead, if this container preloads. Called when it is first drawn,
    /// so a file that is itself only being preloaded does not preload its renditions too.
    /// </summary>
    public void StartPreloading()
    {
        if (!PreloadsNeighbours || Volatile.Read(ref _preloadStarted))
            return;

        lock (_gate)
        {
            _preloadStarted = true;
            SchedulePreload();
        }
    }

    /// <summary>
    /// Decodes the nearest neighbor of the selection that is not decoded yet, one at a time and
    /// never while a requested rendition is on its way. Only what fits the budget as it stands is
    /// preloaded: nothing is trimmed to make room, so preloads cannot push one another out.
    /// Call under the lock.
    /// </summary>
    private void SchedulePreload()
    {
        if (!_preloadStarted || _provider is null || _disposed || _pendingIndex >= 0)
            return;

        int?[] neighbours = [StepFrom(+1), StepFrom(-1)];
        var wanted = neighbours.OfType<int>().FirstOrDefault(i => _contents[i] is null && !_failures.ContainsKey(i), -1);

        if (_preloadIndex >= 0)
        {
            if (neighbours.Contains(_preloadIndex))
                return;

            CancelPreload();
        }

        if (wanted < 0 || !Fits(wanted))
            return;

        _preload = new CancellationTokenSource();
        _preloadIndex = wanted;

        var cts = _preload;
        _ = Task.Run(() => Fetch(wanted, cts.Token), CancellationToken.None);
    }

    private void CancelPreload()
    {
        _preload?.Cancel();
        _preload = null;
        _preloadIndex = -1;
    }

    /// <summary>
    /// Whether a rendition is expected to fit the budget beside what is resident. When its size is
    /// not known yet, only while under half the budget is used.
    /// </summary>
    private bool Fits(int index)
    {
        var resident = _contents.Sum(c => c?.ByteSize ?? 0);
        var variant = Variants[index];

        return variant is { Width: > 0, Height: > 0 }
            ? resident + (long)variant.Width * variant.Height * 4 <= _residentByteBudget
            : resident <= _residentByteBudget / 2;
    }
}