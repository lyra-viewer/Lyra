using Lyra.Common;
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
            composite.ElapsedMs,
            composite.DecodeTimeEstimated,
            composite.TransferBytesTotal,
            composite.TransferBytesRead,
            SourceThroughputEstimator.EstimateTransfer(composite.FileInfo.FullName)
        );
    }
}

/// <summary>
/// Turns a load in progress into a bar, from the two halves of it that are measured separately:
/// the bytes coming from storage and the decode that follows.
/// </summary>
public sealed class LoadProgressPresenter
{
    /// <summary>
    /// How long a load must have been running before a bar appears.
    /// </summary>
    private const double ShowAfterMs = 300;

    /// <summary>
    /// The bar approaches this and waits rather than reaching the end early. Sitting full while
    /// the image is still not on screen looks stuck; sitting just short of full looks busy.
    /// </summary>
    private const float Ceiling = 0.97f;

    private object? _tracked;
    private float _value;

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

        if (Fraction(snapshot) is not { } fraction)
            return new LoadProgress(Visible: true, Value: 0, Indeterminate: true);

        _value = Math.Max(_value, fraction);
        return new LoadProgress(Visible: true, Value: _value, Indeterminate: false);
    }

    private void Reset(object? identity)
    {
        _tracked = identity;
        _value = 0;
    }

    private static float? Fraction(LoadSnapshot s)
    {
        if (s.DecodeEstimateMs <= 0 || s.BytesTotal <= 0 || s.Source is not { } source)
            return null;

        var transferMs = source.MsFor(s.BytesTotal);
        var totalMs = transferMs + s.DecodeEstimateMs;
        if (totalMs <= 0)
            return null;

        var byteShare = (float)(transferMs / totalMs) * Math.Clamp((float)s.BytesRead / s.BytesTotal, 0f, 1f);
        var timeShare = (float)(s.ElapsedMs / totalMs);

        return Math.Min(Ceiling, Math.Max(byteShare, timeShare));
    }
}