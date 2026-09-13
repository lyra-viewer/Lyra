using Lyra.Imaging.Content;

namespace Lyra.Imaging.Decoding.Support;

internal static class CompositeRead
{
    public static byte[] ReadAllBytes(this Composite composite, CancellationToken ct)
    {
        var data = DecoderIO.ReadAllBytes(composite.FileInfo.FullName, ct, out var elapsedMs, composite.ReportTransferred);

        composite.CompleteTransfer(data.Length, elapsedMs);

        return data;
    }
}