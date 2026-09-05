namespace Lyra.Common;

/// <summary>
/// The application's read-speed history, one per process. All the behavior is in
/// <see cref="SourceThroughputSamples"/>; this exists so callers do not have to thread an instance
/// through, and so tests can exercise the logic against a history of their own.
/// </summary>
public static class SourceThroughputEstimator
{
    private static readonly SourceThroughputSamples Samples = new();

    /// <inheritdoc cref="SourceThroughputSamples.Record"/>
    public static void RecordTransfer(string path, long bytes, double ms) => Samples.Record(path, bytes, ms);

    /// <inheritdoc cref="SourceThroughputSamples.Estimate"/>
    public static TransferEstimate? EstimateTransfer(string path) => Samples.Estimate(path);
}
