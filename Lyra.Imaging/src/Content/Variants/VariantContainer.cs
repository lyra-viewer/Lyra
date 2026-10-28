using System.Diagnostics;
using Lyra.Common;
using Lyra.Imaging.Content.Status;

namespace Lyra.Imaging.Content.Variants;

/// <param name="Identity">Stands for one request, so a new request is a new load to the progress bar.</param>
public readonly record struct VariantWait(object Identity, int Index, double ElapsedMs);

/// <summary>
/// The renditions a file holds - pages, frames, sizes, a model's textures and contact sheets -
/// each any kind of content, decoded up front or on demand within a byte budget. Holds the one on
/// screen, may preload its neighbours, and lends decoded ones out for other renditions to reuse.
/// </summary>
public sealed partial class VariantContainer : ICompositeContent
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
    private long _pendingSince;
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
    public VariantContainer(IReadOnlyList<ImageVariant> variants, List<ICompositeContent> contents, int active)
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
    public VariantContainer(IReadOnlyList<ImageVariant> variants, int active, ICompositeContent first, IVariantProvider provider, long residentByteBudget)
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

    public IReadOnlyList<ImageVariant> Variants { get; private set; }

    /// <summary>What the renditions are. Defaults to the icon case: sizes of one image.</summary>
    public VariantKind Kind { get; init; } = VariantKind.Variants;

    private IReadOnlyList<int>? _stepOrder;

    /// <summary>
    /// The renditions stepped through one at a time, in order. All of them unless the creator says
    /// otherwise, as a model leaves out its missing textures and its contact sheets.
    /// </summary>
    public IReadOnlyList<int> StepOrder
    {
        get => _stepOrder ??= [.. Enumerable.Range(0, Variants.Count)];
        init => _stepOrder = [.. value.Where(index => index >= 0 && index < Variants.Count)];
    }

    /// <summary>The rendition <paramref name="steps"/> away from the selected one in <see cref="StepOrder"/>, or null past either end.</summary>
    public int? StepFrom(int steps)
    {
        var order = StepOrder;
        if (order.Count == 0)
            return null;

        var at = IndexOf(order, ActiveIndex);
        var target = at < 0
            ? steps > 0 ? 0 : order.Count - 1
            : at + steps;

        return target >= 0 && target < order.Count && order[target] != ActiveIndex ? order[target] : null;
    }

    private static int IndexOf(IReadOnlyList<int> order, int index)
    {
        for (var i = 0; i < order.Count; i++)
        {
            if (order[i] == index)
                return i;
        }

        return -1;
    }

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

    /// <summary>The rendition asked for and not arrived yet, and for how long, or null.</summary>
    public VariantWait? Waiting
    {
        get
        {
            lock (_gate)
                return _pendingIndex >= 0 && _pending is { } request
                    ? new VariantWait(request, _pendingIndex, Stopwatch.GetElapsedTime(_pendingSince).TotalMilliseconds)
                    : null;
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

    /// <summary>
    /// Records that a rendition cannot be shown, found out before anyone asked for it, such as a
    /// texture whose file is missing. Unlike a failed request it is not reported as the last failure.
    /// </summary>
    public void RecordUnavailable(int index, LoadFailure failure)
    {
        if (index < 0 || index >= Variants.Count)
            return;

        lock (_gate)
        {
            _failures[index] = failure;
            Interlocked.Increment(ref _failureVersion);
        }
    }

    /// <summary>
    /// Replaces a rendition's description, for renditions whose size is only known once decoded.
    /// Safe from any thread; a description equal to the current one changes nothing.
    /// </summary>
    public void Redescribe(int index, ImageVariant variant)
    {
        ArgumentNullException.ThrowIfNull(variant);

        lock (_gate)
        {
            if (_disposed || index < 0 || index >= Variants.Count || Variants[index] == variant)
                return;

            Variants = [.. Variants.Select((existing, i) => i == index ? variant : existing)];
        }
    }

    /// <summary>Raised on a background thread when a requested rendition becomes drawable.</summary>
    public event Action<VariantContainer>? VariantReady;

    /// <summary>Raised on a background thread when a requested rendition could not be decoded.</summary>
    public event Action<VariantContainer>? VariantFailed;

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
            _pendingIndex = index;
            _pendingSince = Stopwatch.GetTimestamp();

            // Already on its way: wait for that rather than start again.
            if (_preloadIndex == index && _preload is { } preload)
            {
                _pending = preload;
                return true;
            }

            CancelPreload();

            _pending = new CancellationTokenSource();
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
                Logger.Warning(typeof(VariantContainer), $"{Variants[index].Label}: {failure.Message}. Detail: {failure.Detail}");
            else
                Logger.Error(typeof(VariantContainer), $"{Variants[index].Label} failed to decode: {ex}");
        }

        if (decoded is null && failure is null && !ct.IsCancellationRequested)
            failure = LoadFailure.NothingDecoded;

        var publish = false;
        var failed = false;

        lock (_gate)
        {
            var requested = _pendingIndex == index;
            var preloaded = _preloadIndex == index;

            if (preloaded)
            {
                _preload = null;
                _preloadIndex = -1;
            }

            if (decoded is null || _disposed || ct.IsCancellationRequested || !(requested || preloaded))
            {
                decoded?.Dispose();

                if (requested)
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
                else if (preloaded && failure is not null && !_disposed && !ct.IsCancellationRequested)
                {
                    // Listed as failed, but not reported as the last failure: nobody asked for it.
                    _failures[index] = failure;
                    Interlocked.Increment(ref _failureVersion);
                }
            }
            else if (requested)
            {
                _contents[index] = decoded;
                _pendingIndex = -1;
                Show(index);
                Trim();
                publish = true;
            }
            else if (_contents[index] is null && _contents.Sum(c => c?.ByteSize ?? 0) + decoded.ByteSize <= _residentByteBudget)
            {
                // Not yet seen, so first to go when room is needed.
                _contents[index] = decoded;
                _recent.Insert(0, index);
            }
            else
            {
                decoded.Dispose();
            }

            SchedulePreload();
        }

        if (publish)
            VariantReady?.Invoke(this);
        else if (failed)
            VariantFailed?.Invoke(this);
    }

    public void ReplaceVariant(int index, ICompositeContent content, ImageVariant? variant = null)
    {
        ArgumentNullException.ThrowIfNull(content);

        lock (_gate)
        {
            if (_disposed || index < 0 || index >= _contents.Count)
            {
                content.Dispose();
                return;
            }

            if (_contents[index] is { } old)
                _retired.Add(old);

            _contents[index] = content;

            if (variant is not null)
                Variants = [.. Variants.Select((existing, i) => i == index ? variant : existing)];

            if (_failures.Remove(index))
                Interlocked.Increment(ref _failureVersion);

            // A fetch of the same rendition, asked for while the stand-in was trimmed, is now moot.
            if (_pendingIndex == index)
            {
                _pending?.Cancel();
                Show(index);
            }

            // Just arrived, so the last thing to trim.
            _recent.Remove(index);
            _recent.Add(index);

            Trim();
        }

        VariantReady?.Invoke(this);
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

        SchedulePreload();
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

            // Lent content stays until it comes back.
            retired = [.. _retired.Where(content => !_leases.ContainsKey(content))];
            _retired.RemoveAll(content => !_leases.ContainsKey(content));
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
            CancelPreload();

            foreach (var content in _contents.OfType<ICompositeContent>())
                _retired.Add(content);

            _contents.Clear();
            _contents.AddRange(Enumerable.Repeat<ICompositeContent?>(null, Variants.Count));

            // Lent content is disposed when it comes back.
            foreach (var content in _retired.Where(content => !_leases.ContainsKey(content)))
                content.Dispose();

            _retired.RemoveAll(content => !_leases.ContainsKey(content));
            _recent.Clear();
        }

        (_provider as IDisposable)?.Dispose();
    }
}