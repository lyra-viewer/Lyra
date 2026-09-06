namespace Lyra.Common;

public static class Formatters
{
    private const long KB = 1024;
    private const long MB = KB * 1024;
    
    public static string SizeToStr(long? bytes)
    {
        if (bytes is not { } value) 
            return "n/a";

        return value switch
        {
            >= 100 * MB => $"{value / MB} MB",
            >= 2 * MB   => $"{value / (double)MB:0.#} MB",
            >= KB       => $"{value / KB} kB",
            _           => $"{value} bytes"
        };
    }

    public static string MsToStr(double? ms) => ms switch
    {
        null => "n/a",
        < 10 => ms.Value.ToString("0.00"),
        _    => ms.Value.ToString("0")
    };
}