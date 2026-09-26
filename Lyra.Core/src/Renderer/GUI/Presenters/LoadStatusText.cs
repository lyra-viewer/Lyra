using System.Globalization;

namespace Lyra.Renderer.GUI.Presenters;

public static class LoadStatusText
{
    public const string Plain = "Loading...";
    
    internal const double ShowBytesAfterMs = 1200;

    /// <summary>Digits kept for a count read with no total to size it against.</summary>
    private const int UnsizedDigits = 5;

    private const long KB = 1024;
    private const long MB = 1024 * KB;

    public static string For(LoadSnapshot load)
    {
        if (!load.Active || load.ElapsedMs < ShowBytesAfterMs || load.BytesRead <= 0)
            return Plain;

        if (load.BytesTotal <= 0)
        {
            var (unsizedDivisor, unsizedUnit) = UnitFor(load.BytesRead);
            return $"{Plain} {Whole(load.BytesRead, unsizedDivisor, UnsizedDigits)} {unsizedUnit} read";
        }

        var (divisor, unit) = UnitFor(load.BytesTotal);
        var total = Whole(load.BytesTotal, divisor, 0);
        
        if (load.BytesRead >= load.BytesTotal)
            return $"{Plain} {total} {unit} read, decoding";
        
        var read = Whole(load.BytesRead, divisor, total.Length);
        var percent = (load.BytesRead * 100 / load.BytesTotal).ToString(CultureInfo.InvariantCulture).PadLeft(3);

        return $"{Plain} {read} / {total} {unit} ({percent}%)";
    }

    private static (long Divisor, string Unit) UnitFor(long bytes) => bytes >= MB ? (MB, "MB") : (KB, "kB");

    private static string Whole(long bytes, long divisor, int width) => (bytes / divisor).ToString(CultureInfo.InvariantCulture).PadLeft(width);
}
