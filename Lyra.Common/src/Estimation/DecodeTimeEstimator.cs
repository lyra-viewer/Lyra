namespace Lyra.Common.Estimation;

public static class DecodeTimeEstimator
{
    private static readonly Lazy<DecodeTimeSamples> Samples = new(() => new DecodeTimeSamples(LyraIO.GetLoadTimeFile()), LazyThreadSafetyMode.ExecutionAndPublication);

    public static void RecordDecodeTime(string extension, long sizeInBytes, long? pixels, double ms, bool includesTransfer = false)
        => Samples.Value.Record(extension, sizeInBytes, pixels, ms, includesTransfer);

    public static LoadEstimate EstimateDecodeTime(string extension, long sizeInBytes, long? pixels = null)
        => Samples.Value.Estimate(extension, sizeInBytes, pixels);

    public static void SaveTimeDataToFile() => Samples.Value.Save();
}
