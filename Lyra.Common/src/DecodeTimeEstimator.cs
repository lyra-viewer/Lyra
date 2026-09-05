namespace Lyra.Common;

public static class DecodeTimeEstimator
{
    private static readonly Lazy<DecodeTimeSamples> Samples = new(() => new DecodeTimeSamples(LyraIO.GetLoadTimeFile()), LazyThreadSafetyMode.ExecutionAndPublication);

    public static void RecordDecodeTime(string extension, long sizeInBytes, double ms) => Samples.Value.Record(extension, sizeInBytes, ms);

    public static double EstimateDecodeTime(string extension, long sizeInBytes) => Samples.Value.Estimate(extension, sizeInBytes);

    public static void SaveTimeDataToFile() => Samples.Value.Save();
}
