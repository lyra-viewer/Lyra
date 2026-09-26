using Lyra.Common.Estimation;
using Lyra.Imaging.Content;

namespace Lyra.Renderer.GUI.Presenters;

public readonly record struct LoadProgress(bool Visible, float Value, bool Indeterminate);

public readonly record struct LoadSnapshot(object? Identity, bool Active, double ElapsedMs, double DecodeEstimateMs, long BytesTotal, long BytesRead, TransferEstimate? Source)
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
            composite.Timing.TransferBytesTotal,
            composite.Timing.TransferBytesRead,
            SourceThroughputEstimator.EstimateTransfer(composite.FileInfo.FullName)
        );
    }
}

/// <summary>
/// Turns a load into a bar: the transfer part follows the bytes read, the decode part follows the
/// decode estimate. The bar rests briefly at the end, then sweeps for as long as the load outlives it.
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

        var measured = Fraction(snapshot);
        
        if (!_followingBytes && BytesArriving(snapshot) && measured is { } first)
        {
            _followingBytes = true;
            _estimateSpent = false;
            _fullSinceMs = null;
            _value = first;
        }

        if (_estimateSpent || measured is not { } fraction)
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
        _transferDoneAtMs = null;
        _shareAtTransferDone = null;
        _followingBytes = false;
    }

    private bool _followingBytes;

    private static bool BytesArriving(LoadSnapshot s) => s.BytesTotal > 0 && s.BytesRead > 0 && s.BytesRead < s.BytesTotal;

    private double? _transferDoneAtMs;

    private double? _shareAtTransferDone;

    private float? Fraction(LoadSnapshot s)
    {
        if (s.BytesTotal > 0 && s.BytesRead > 0)
            return FollowingBytes(s);

        if (s.DecodeEstimateMs <= 0 || s.BytesTotal <= 0 || s.Source is not { } source)
            return null;

        var transferMs = source.MsFor(s.BytesTotal);
        var totalMs = transferMs + s.DecodeEstimateMs;

        return totalMs > 0 ? Math.Min(1f, (float)(s.ElapsedMs / totalMs)) : null;
    }

    private float FollowingBytes(LoadSnapshot s)
    {
        var share = TransferShare(s);

        if (s.BytesRead < s.BytesTotal)
            return (float)(share * s.BytesRead / s.BytesTotal);

        var decodeMs = s.DecodeEstimateMs;
        if (share >= 1 || decodeMs <= 0)
            return 1f;
        
        _transferDoneAtMs ??= s.ElapsedMs;
        _shareAtTransferDone ??= share;

        var decoded = Math.Clamp((s.ElapsedMs - _transferDoneAtMs.Value) / decodeMs, 0, 1);
        return (float)Math.Min(1, _shareAtTransferDone.Value + (1 - _shareAtTransferDone.Value) * decoded);
    }

    /// <summary>Predicted from the source's measured speed, or from this load's own rate until there is one.</summary>
    private static double TransferShare(LoadSnapshot s)
    {
        var transferMs = s.Source is { } source
            ? source.MsFor(s.BytesTotal)
            : s.ElapsedMs * s.BytesTotal / s.BytesRead;

        var decodeMs = s.DecodeEstimateMs;

        return decodeMs <= 0 || transferMs <= 0 ? 1 : transferMs / (transferMs + decodeMs);
    }
}
