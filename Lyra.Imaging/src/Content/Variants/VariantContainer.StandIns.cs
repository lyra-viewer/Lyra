using Lyra.Common;

namespace Lyra.Imaging.Content.Variants;

/// <summary>
/// Stand-ins: reduced copies of renditions dropped from the budget, shown at once when one is asked
/// for again while its full decode runs. Kept within their own budget, least recently used first out.
/// </summary>
public sealed partial class VariantContainer
{
    private readonly Dictionary<int, ICompositeContent> _standIns = new();

    /// <summary>Most recently kept or shown last.</summary>
    private readonly List<int> _standInRecent = [];

    /// <summary>The last rendition shown whole, to go back to when a stand-in's full decode fails.</summary>
    private int _wholeIndex;

    private readonly record struct Dropped(int Index, ICompositeContent Content);

    /// <summary>Makes the reduced copy kept of a dropped rendition, or null to keep none. Unset keeps none.</summary>
    public Func<ICompositeContent, ICompositeContent?>? MakeStandIn { get; init; }

    public long StandInBudgetBytes { get; init; }

    private bool KeepsStandIns => MakeStandIn is not null && StandInBudgetBytes > 0;

    /// <summary>Whether a reduced copy of the rendition is kept.</summary>
    public bool HasStandIn(int index)
    {
        lock (_gate)
            return _standIns.ContainsKey(index);
    }

    /// <summary>
    /// Keeps a reduced copy of <paramref name="full"/> for a rendition that is not resident, as a
    /// contact sheet does with the textures it decodes for its cells. The caller keeps ownership.
    /// </summary>
    public void OfferStandIn(int index, ICompositeContent full)
    {
        ArgumentNullException.ThrowIfNull(full);

        lock (_gate)
        {
            if (!KeepsStandIns || _disposed || index < 0 || index >= _contents.Count || _contents[index] is not null || _standIns.ContainsKey(index))
                return;
        }

        if (Reduce(index, full) is { } copy)
            AddStandIn(index, copy);
    }

    /// <summary>Keeps a reduced copy made before the container existed. The container takes ownership.</summary>
    public void KeepStandIn(int index, ICompositeContent copy)
    {
        ArgumentNullException.ThrowIfNull(copy);

        if (!KeepsStandIns || index < 0 || index >= Variants.Count)
        {
            copy.Dispose();
            return;
        }

        AddStandIn(index, copy);
    }

    private List<Dropped>? LeaseForStandIns(List<Dropped>? dropped)
    {
        if (dropped is null || !KeepsStandIns)
            return null;

        foreach (var (_, content) in dropped)
            _leases[content] = _leases.GetValueOrDefault(content) + 1;

        return dropped;
    }

    private void KeepStandIns(List<Dropped>? dropped)
    {
        if (dropped is null)
            return;

        foreach (var (index, content) in dropped)
        {
            ICompositeContent? copy;
            try
            {
                copy = Reduce(index, content);
            }
            finally
            {
                Return(content);
            }

            if (copy is not null)
                AddStandIn(index, copy);
        }
    }

    private ICompositeContent? Reduce(int index, ICompositeContent full)
    {
        try
        {
            return MakeStandIn!(full);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            Logger.Warning(typeof(VariantContainer), $"{Variants[index].Label}: no reduced copy kept: {ex.Message}");
            return null;
        }
    }

    private void AddStandIn(int index, ICompositeContent copy)
    {
        lock (_gate)
        {
            // Decoded again meanwhile, or never going to fit.
            if (_disposed || _contents[index] is not null || copy.ByteSize > StandInBudgetBytes)
            {
                copy.Dispose();
                return;
            }

            DropStandIn(index);

            _standIns[index] = copy;
            _standInRecent.Add(index);

            TrimStandIns();
        }
    }

    private void DropStandIn(int index)
    {
        if (!_standIns.Remove(index, out var standIn))
            return;

        _standInRecent.Remove(index);
        _retired.Add(standIn);
    }

    private void TouchStandIn(int index)
    {
        if (_standInRecent.Remove(index))
            _standInRecent.Add(index);
    }

    private void TrimStandIns()
    {
        var total = _standIns.Values.Sum(c => c.ByteSize);
        for (var i = 0; i < _standInRecent.Count && total > StandInBudgetBytes; i++)
        {
            var index = _standInRecent[i];
            if (index == _shownIndex && _contents[index] is null)
                continue;

            total -= _standIns[index].ByteSize;
            _retired.Add(_standIns[index]);
            _standIns.Remove(index);

            _standInRecent.RemoveAt(i);
            i--;
        }
    }
}