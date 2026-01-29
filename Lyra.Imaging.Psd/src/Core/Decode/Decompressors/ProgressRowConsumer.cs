namespace Lyra.Imaging.Psd.Core.Decode.Decompressors;

public sealed class ProgressRowConsumer : IPlaneRowConsumer
{
    private readonly IPlaneRowConsumer _inner;
    private readonly Action<int> _reportRowsCompleted;

    private int _lastReportedY = -1;

    public ProgressRowConsumer(IPlaneRowConsumer inner, Action<int> reportRowsCompleted)
    {
        _inner = inner;
        _reportRowsCompleted = reportRowsCompleted;
    }

    public void ConsumeRow(int planeIndex, int y, ReadOnlySpan<byte> row)
    {
        _inner.ConsumeRow(planeIndex, y, row);

        // Report once per row
        if (planeIndex == 0 && y != _lastReportedY)
        {
            _lastReportedY = y;
            _reportRowsCompleted(y + 1);
        }
    }
}
