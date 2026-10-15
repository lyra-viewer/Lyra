using System.Globalization;

namespace Lyra.Common;

public static class NumberCulture
{
    public static void Apply()
    {
        var culture = WithDecimalPoint(CultureInfo.CurrentCulture);
        CultureInfo.CurrentCulture = culture;
        CultureInfo.DefaultThreadCurrentCulture = culture;
    }

    public static CultureInfo WithDecimalPoint(CultureInfo culture)
    {
        var copy = (CultureInfo)culture.Clone();
        copy.NumberFormat = (NumberFormatInfo)CultureInfo.InvariantCulture.NumberFormat.Clone();
        return copy;
    }
}
