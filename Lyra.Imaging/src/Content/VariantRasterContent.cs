using Lyra.Common;

namespace Lyra.Imaging.Content;

public sealed class VariantRasterContent : ICompositeContent
{
    private readonly List<ICompositeContent?> _contents;
    private readonly IVariantProvider? _provider;
    private readonly long _residentByteBudget;

    /// <summary>Most recently shown last. Only used for the on-demand form.</summary>
    private readonly List<int> _recent = [];

    private readonly Lock _gate = new();
    private CancellationTokenSource? _pending;
    private int _pendingIndex = -1;
    private bool _disposed;

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

    /// <summary>
    /// What the renditions are, for the panel heading. Defaults to the icon case; a decoder whose
    /// container holds something else - the pages of a document - says so.
    /// </summary>
    public string GroupLabel { get; init; } = "VARIANTS";

    /// <summary>
    /// Whether the set is long enough to want a jump control rather than only a scrollable list.
    /// </summary>
    public bool IsLong => Variants.Count > 12;

    /// <summary>What the interface shows as selected, which a pending decode has already moved.</summary>
    public int ActiveIndex { get; private set; }

    private int _shownIndex;

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

    /// <summary>Raised on a background thread when a requested rendition becomes drawable.</summary>
    public event Action<VariantRasterContent>? VariantReady;

    public bool IsResolutionIndependent => Active?.IsResolutionIndependent == true;

    public float? DecodedWidth => Active is RasterLargeContent large ? large.FullWidth : Active?.DecodedWidth;
    public float? DecodedHeight => Active is RasterLargeContent large ? large.FullHeight : Active?.DecodedHeight;

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

        try
        {
            decoded = _provider!.Decode(index, ct);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            Logger.Warning($"[VariantRasterContent] Rendition {index} failed to decode: {ex.Message}");
        }

        var publish = false;

        lock (_gate)
        {
            if (decoded is null || _disposed || ct.IsCancellationRequested || _pendingIndex != index)
            {
                decoded?.Dispose();

                if (_pendingIndex == index)
                    _pendingIndex = -1;
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
    }

    /// <summary>Marks a rendition as the one on screen and the most recently used. Call under the lock.</summary>
    private void Show(int index)
    {
        _shownIndex = index;
        _pendingIndex = -1;

        _recent.Remove(index);
        _recent.Add(index);
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
            content.Dispose();
            _contents[index] = null;

            _recent.RemoveAt(i);
            i--;
        }
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

            _recent.Clear();
        }
    }
}