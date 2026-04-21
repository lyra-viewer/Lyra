namespace Lyra.Renderer.GUI.Support;

public static class Formatters
{
    public static string SizeToStr(long bytes)
    {
        const long kB = 1024;
        const long MB = kB * 1024;

        return bytes switch
        {
            >= 100 * MB => $"{bytes / MB} MB",
            >= 2 * MB => $"{Math.Round(bytes / (double)MB, 1)} MB",
            >= kB => $"{bytes / kB} kB",
            _ => $"{bytes} bytes"
        };
    }

    public static string MsToStr(double? ms) => ms switch
    {
        null => "n/a",
        < 10 => ms.Value.ToString("0.00"),
        _ => ms.Value.ToString("0")
    };
}