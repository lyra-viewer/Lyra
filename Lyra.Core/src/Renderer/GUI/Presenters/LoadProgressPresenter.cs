using Lyra.Common.Estimation;
using Lyra.Imaging.Content;

namespace Lyra.Renderer.GUI.Presenters;

public readonly record struct LoadProgress(bool Visible, float Value, bool Indeterminate);

public readonly record struct LoadSnapshot(object? Identity, bool Active, double ElapsedMs, double DecodeEstimateMs, bool EstimateIncludesTransfer, long BytesTotal, long BytesRead, TransferEstimate? Source)
{
    public static LoadSnapshot Of(Composite? composite)
    {
        if (composite is null || composite.State != CompositeState.Loading)
            return default;

        return new LoadSnapshot(
            composite,
            Active: true,
            composite.Timing.ElapsedMs,
            composite.Timing.DecodeEstimateMs,
            composite.Timing.EstimateIncludesTransfer,
            composite.Timing.TransferBytesTotal,
            composite.Timing.TransferBytesRead,
            SourceThroughputEstimator.EstimateTransfer(composite.FileInfo.FullName)
        );
    }
}

/// <summary>
/// Turns a load in progress into a bar, from the two halves of it that are measured separately:
/// the bytes coming from storage and the decode that follows.
///
/// The bar fills against the prediction, rests briefly at the end, and then sweeps for as long as
/// the load outlives it.
/// </summary>
public sealed class LoadProgressPresenter
{
    /// <summary>
    /// How long a load must have been running before a bar appears.
    /// </summary>
    private const double ShowAfterMs = 300;

    /// <summary>
    /// How long the bar rests at the end before admitting the estimate is spent.
    /// </summary>
    private const double DwellAtEndMs = 2000;

    private object? _tracked;
    private float _value;

    /// <summary>Elapsed time at which the bar first reached the end, for the rest to be measured from.</summary>
    private double? _fullSinceMs;
    
    private bool _partialShown;
    
    private bool _estimateSpent;

    public LoadProgress Update(LoadSnapshot snapshot)
    {
        if (!snapshot.Active)
        {
            Reset(null);
            return default;
        }

        if (!ReferenceEquals(_tracked, snapshot.Identity))
            Reset(snapshot.Identity);

        if (snapshot.ElapsedMs < ShowAfterMs)
            return default;

        if (_estimateSpent || Fraction(snapshot) is not { } fraction)
            return Sweeping();

        _value = Math.Max(_value, fraction);

        if (_value < 1f)
        {
            _partialShown = true;
            return new LoadProgress(Visible: true, Value: _value, Indeterminate: false);
        }

        // Full on the very frame it appeared: the estimate was already spent before the bar was
        // shown, so there is no completed fill for the rest at the end to be about.
        if (!_partialShown)
            return GiveUp();

        _fullSinceMs ??= snapshot.ElapsedMs;

        return snapshot.ElapsedMs - _fullSinceMs.Value < DwellAtEndMs
            ? new LoadProgress(Visible: true, Value: 1f, Indeterminate: false)
            : GiveUp();
    }

    private LoadProgress GiveUp()
    {
        _estimateSpent = true;
        return Sweeping();
    }
    
    private LoadProgress Sweeping() => new(Visible: true, Value: _value, Indeterminate: true);

    private void Reset(object? identity)
    {
        _tracked = identity;
        _value = 0;
        _fullSinceMs = null;
        _partialShown = false;
        _estimateSpent = false;
    }

    private static float? Fraction(LoadSnapshot s)
    {
        if (s.DecodeEstimateMs <= 0 || s.BytesTotal <= 0 || s.Source is not { } source)
            return null;
        
        var transferMs = s.EstimateIncludesTransfer ? 0 : source.MsFor(s.BytesTotal);
        var totalMs = transferMs + s.DecodeEstimateMs;
        if (totalMs <= 0)
            return null;

        var byteShare = (float)(transferMs / totalMs) * Math.Clamp((float)s.BytesRead / s.BytesTotal, 0f, 1f);
        var timeShare = (float)(s.ElapsedMs / totalMs);

        return Math.Min(1f, Math.Max(byteShare, timeShare));
    }
}