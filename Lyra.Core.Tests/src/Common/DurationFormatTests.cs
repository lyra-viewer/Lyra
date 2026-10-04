using System.Globalization;
using Lyra.Common;
using Xunit;

namespace Lyra.Core.Tests.Common;

public class DurationFormatTests
{
    [Theory]
    [InlineData(0.0, "0.00 ms")]
    [InlineData(4.271, "4.27 ms")]
    [InlineData(9.994, "9.99 ms")]
    [InlineData(9.996, "10 ms")]
    [InlineData(350.0, "350 ms")]
    [InlineData(999.4, "999 ms")]
    [InlineData(999.6, "1.00 s")]
    [InlineData(58_550.0, "58.55 s")]
    public void ADuration_CarriesItsUnit_AndNeverRoundsIntoTheNextOne(double ms, string expected)
    {
        var separator = CultureInfo.CurrentCulture.NumberFormat.NumberDecimalSeparator;
        Assert.Equal(expected.Replace(".", separator), Formatters.DurationToStr(ms));
    }

    [Fact]
    public void NoDuration_IsNotApplicable() =>
        Assert.Equal("n/a", Formatters.DurationToStr(null));
}
