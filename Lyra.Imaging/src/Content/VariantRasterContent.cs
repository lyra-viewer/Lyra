using Lyra.Common;

namespace Lyra.Imaging.Content;

public sealed class VariantRasterContent : ICompositeContent
{
    private readonly List<ICompositeContent?> _contents;
    private readonly IVariantProvider? _provider;
    private readonly long _residentByteBudget;

    /// <summary>Most recently shown last. Only used for the on-demand form.</summary>
    private readonly List<int> _recent = [];
    
    private readonly List<ICompositeContent> _retired = [];

    private readonly Lock _gate = new();
    private CancellationTokenSource? _pending;
    private int _pendingIndex = -1;
    private bool _disposed;

    private readonly Dictionary<int, LoadFailure> _failures = new();
    private int _lastFailedIndex = -1;
    private int _failureVersion;

    private readonly Dictionary<int, LoadWarning> _warnings = new();
    private int _warningVersion;

    /// <summary>
    /// Every rendition decoded up front. For containers small enough that laziness would only add
    /// failure modes.
    /// </summary>
    public VariantRasterContent(IReadOnlyList<ImageVariant> variants, List<ICompositeContent> contents, int active)
    {
        ArgumentNullException.ThrowIfNull(variants);
        ArgumentNullException.ThrowIfNull(contents);

        if (variants.Count == 0 || variants.Count != contents.Count)
            throw new ArgumentException("Variant list and decoded content list must be non-empty and the same length.");

        Variants = variants;
        _contents = [.. contents];
        ActiveIndex = Math.Clamp(active, 0, variants.Count - 1);
        _shownIndex = ActiveIndex;
    }

    /// <summary>
    /// Renditions decoded on demand, with <paramref name="first"/> already in hand so there is
    /// something to draw immediately.
    /// </summary>
    public VariantRasterContent(IReadOnlyList<ImageVariant> variants, int active, ICompositeContent first, IVariantProvider provider, long residentByteBudget)
    {
        ArgumentNullException.ThrowIfNull(variants);
        ArgumentNullException.ThrowIfNull(first);
        ArgumentNullException.ThrowIfNull(provider);

        if (variants.Count == 0)
            throw new ArgumentException("Variant list must not be empty.", nameof(variants));

        Variants = variants;
        _provider = provider;
        _residentByteBudget = Math.Max(0, residentByteBudget);

        _contents = [.. Enumerable.Repeat<ICompositeContent?>(null, variants.Count)];

        ActiveIndex = Math.Clamp(active, 0, variants.Count - 1);
        _shownIndex = ActiveIndex;

        _contents[ActiveIndex] = first;
        _recent.Add(ActiveIndex);
    }

    public IReadOnlyList<ImageVariant> Variants { get; }

    /// <summary>What the renditions are. Defaults to the icon case;</summary>
    public VariantKind Kind { get; init; } = VariantKind.Variants;

    /// <summary>What the interface shows as selected, which a pending decode has already moved.</summary>
    public int ActiveIndex { get; private set; }

    private int _shownIndex;

    /// <summary>The index of the rendition on screen, which lags <see cref="ActiveIndex"/> while one decodes.</summary>
    public int ShownIndex
    {
        get
        {
            lock (_gate)
                return _shownIndex;
        }
    }

    /// <summary>
    /// The rendition currently on screen. Follows <see cref="ActiveIndex"/> as soon as that one is
    /// decoded, and until then stays on the last one that was - so the view never goes blank.
    /// </summary>
    public ICompositeContent? Active
    {
        get
        {
            lock (_gate)
                return _shownIndex < _contents.Count
                    ? _contents[_shownIndex] ?? _contents.FirstOrDefault(c => c is not null)
                    : null;
        }
    }

    /// <summary>Whether a rendition has been asked for and has not arrived yet.</summary>
    public bool IsWaiting
    {
        get
        {
            lock (_gate)
                return _pendingIndex >= 0;
        }
    }

    public LoadFailure? FailureOf(int index)
    {
        lock (_gate)
            return _failures.GetValueOrDefault(index);
    }

    /// <summary>The rendition last asked for that failed, until another is shown or asked for.</summary>
    public (int Index, LoadFailure Failure)? LastFailure
    {
        get
        {
            lock (_gate)
                return _lastFailedIndex >= 0 && _failures.TryGetValue(_lastFailedIndex, out var failure)
                    ? (_lastFailedIndex, failure)
                    : null;
        }
    }

    /// <summary>Changes whenever a rendition's failure is recorded or cleared.</summary>
    public int FailureVersion => Volatile.Read(ref _failureVersion);

    /// <summary>How a rendition is shown incomplete, or null when it is whole or not yet decoded.</summary>
    public LoadWarning? WarningOf(int index)
    {
        lock (_gate)
            return _warnings.GetValueOrDefault(index);
    }

    /// <summary>Changes whenever a rendition's warning is recorded.</summary>
    public int WarningVersion => Volatile.Read(ref _warningVersion);

    /// <summary>
    /// Records that a rendition is shown, but not whole. Unlike a failure it stays when the
    /// rendition is shown, since that is exactly when it applies. Safe from any thread.
    /// </summary>
    public void RecordWarning(int index, LoadWarning warning)
    {
        if (index < 0 || index >= Variants.Count)
            return;

        lock (_gate)
        {
            if (_warnings.TryGetValue(index, out var existing) && existing == warning)
                return;

            _warnings[index] = warning;
            Interlocked.Increment(ref _warningVersion);
        }
    }

    /// <summary>Raised on a background thread when a requested rendition becomes drawable.</summary>
    public event Action<VariantRasterContent>? VariantReady;

    /// <summary>Raised on a background thread when a requested rendition could not be decoded.</summary>
    public event Action<VariantRasterContent>? VariantFailed;

    public bool IsResolutionIndependent => Active?.IsResolutionIndependent == true;

    public float? DecodedWidth => Shown?.Width;
    public float? DecodedHeight => Shown?.Height;

    /// <summary>The description of the rendition currently on screen.</summary>
    private ImageVariant? Shown
    {
        get
        {
            lock (_gate)
                return _shownIndex >= 0 && _shownIndex < Variants.Count ? Variants[_shownIndex] : null;
        }
    }

    /// <summary>What is decoded right now, which for the on-demand form is not the whole document.</summary>
    public long ByteSize
    {
        get
        {
            lock (_gate)
                return _contents.Sum(c => c?.ByteSize ?? 0);
        }
    }

    /// <summary>
    /// Shows the rendition at <paramref name="index"/>, decoding it first if it is not resident.
    /// Returns false when the index is out of range or already selected, so callers can skip
    /// redundant relayout.
    /// </summary>
    public bool Select(int index)
    {
        if (index < 0 || index >= _contents.Count || index == ActiveIndex)
            return false;

        lock (_gate)
        {
            if (_disposed)
                return false;

            if (_contents[index] is not null)
            {
                ActiveIndex = index;
                Show(index);
                return true;
            }

            if (_provider is null)
                return false;

            ActiveIndex = index;
            _lastFailedIndex = -1;

            _pending?.Cancel();
            _pending = new CancellationTokenSource();
            _pendingIndex = index;

            var cts = _pending;
            _ = Task.Run(() => Fetch(index, cts.Token), CancellationToken.None);
        }

        return true;
    }

    private void Fetch(int index, CancellationToken ct)
    {
        ICompositeContent? decoded = null;
        LoadFailure? failure = null;

        try
        {
            decoded = _provider!.Decode(index, ct);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            failure = LoadFailure.From(ex);

            if (failure.IsExpected)
                Logger.Warning(typeof(VariantRasterContent), $"{Variants[index].Label}: {failure.Message}. Detail: {failure.Detail}");
            else
                Logger.Error(typeof(VariantRasterContent), $"{Variants[index].Label} failed to decode: {ex}");
        }

        if (decoded is null && failure is null && !ct.IsCancellationRequested)
            failure = LoadFailure.NothingDecoded;

        var publish = false;
        var failed = false;

        lock (_gate)
        {
            if (decoded is null || _disposed || ct.IsCancellationRequested || _pendingIndex != index)
            {
                decoded?.Dispose();

                if (_pendingIndex == index)
                {
                    _pendingIndex = -1;

                    if (!_disposed)
                    {
                        ActiveIndex = _shownIndex;
                        failed = true;

                        if (failure is not null)
                            RecordFailure(index, failure);
                    }
                }
            }
            else
            {
                _contents[index] = decoded;
                _pendingIndex = -1;
                Show(index);
                Trim();
                publish = true;
            }
        }

        if (publish)
            VariantReady?.Invoke(this);
        else if (failed)
            VariantFailed?.Invoke(this);
    }

    /// <summary>Marks a rendition as the one on screen and the most recently used. Call under the lock.</summary>
    private void Show(int index)
    {
        _shownIndex = index;
        _pendingIndex = -1;
        _lastFailedIndex = -1;

        if (_failures.Remove(index))
            Interlocked.Increment(ref _failureVersion);

        _recent.Remove(index);
        _recent.Add(index);
    }

    private void RecordFailure(int index, LoadFailure failure)
    {
        _failures[index] = failure;
        _lastFailedIndex = index;
        Interlocked.Increment(ref _failureVersion);
    }

    /// <summary>
    /// Drops the least recently shown renditions until the resident total is inside the budget.
    /// Never drops the one on screen, whatever the budget says.
    /// </summary>
    private void Trim()
    {
        if (_provider is null)
            return;

        var resident = _contents.Sum(c => c?.ByteSize ?? 0);
        for (var i = 0; i < _recent.Count && resident > _residentByteBudget; i++)
        {
            var index = _recent[i];
            if (index == _shownIndex || _contents[index] is not { } content)
                continue;

            resident -= content.ByteSize;
            _retired.Add(content);
            _contents[index] = null;

            _recent.RemoveAt(i);
            i--;
        }
    }
    
    public void ReleaseRetired()
    {
        ICompositeContent[] retired;

        lock (_gate)
        {
            if (_retired.Count == 0)
                return;

            retired = [.. _retired];
            _retired.Clear();
        }

        foreach (var content in retired)
            content.Dispose();
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _disposed = true;
            _pending?.Cancel();
            _pending = null;

            for (var i = 0; i < _contents.Count; i++)
            {
                _contents[i]?.Dispose();
                _contents[i] = null;
            }

            foreach (var content in _retired)
                content.Dispose();

            _retired.Clear();
            _recent.Clear();
        }

        (_provider as IDisposable)?.Dispose();
    }
}