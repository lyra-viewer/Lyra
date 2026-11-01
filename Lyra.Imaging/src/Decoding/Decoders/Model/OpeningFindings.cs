using Lyra.Common;
using Lyra.Imaging.Content;
using Lyra.Imaging.Content.Status;
using Lyra.Imaging.Content.Variants;

namespace Lyra.Imaging.Decoding.Decoders.Model;

/// <summary>
/// What decoding the sheet a model opens on finds out about its textures before their container
/// exists: descriptions, warnings and reduced copies, handed to the container once it does.
/// Used by the decoding thread only.
/// </summary>
internal sealed class OpeningFindings(Func<ICompositeContent, ICompositeContent?> makeStandIn, long standInBudgetBytes) : IDisposable
{
    private readonly record struct Learned(ImageVariant Variant, LoadWarning? Warning);

    private readonly Dictionary<int, Learned> _learned = new();
    private readonly Dictionary<int, ICompositeContent> _standIns = new();
    private long _standInBytes;

    public void Learn(int index, ImageVariant variant, LoadWarning? warning) => _learned[index] = new Learned(variant, warning);

    /// <summary>Keeps a reduced copy of <paramref name="full"/> while inside the budget. The caller keeps <paramref name="full"/>.</summary>
    public void KeepStandIn(int index, ICompositeContent full)
    {
        if (_standIns.ContainsKey(index) || _standInBytes >= standInBudgetBytes)
            return;

        ICompositeContent? copy;
        try
        {
            copy = makeStandIn(full);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            Logger.Warning(typeof(OpeningFindings), $"Texture {index}: no reduced copy kept: {ex.Message}");
            return;
        }

        if (copy is null)
            return;

        _standIns[index] = copy;
        _standInBytes += copy.ByteSize;
    }

    /// <summary>Gives everything to <paramref name="owner"/>, which takes the reduced copies.</summary>
    public void HandTo(VariantContainer owner)
    {
        foreach (var (index, learned) in _learned)
        {
            owner.Redescribe(index, learned.Variant);

            if (learned.Warning is { } warning)
                owner.RecordWarning(index, warning);
        }

        foreach (var (index, standIn) in _standIns)
            owner.KeepStandIn(index, standIn);

        _learned.Clear();
        _standIns.Clear();
        _standInBytes = 0;
    }

    public void Dispose()
    {
        foreach (var standIn in _standIns.Values)
            standIn.Dispose();

        _standIns.Clear();
        _standInBytes = 0;
    }
}