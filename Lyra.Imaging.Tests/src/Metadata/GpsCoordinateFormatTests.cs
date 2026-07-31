using Lyra.Imaging.Metadata;
using Xunit;

namespace Lyra.Imaging.Tests.Metadata;

/// <summary>
/// The panel renders latitude and longitude on consecutive rows in a monospace column, so their
/// degree, minute and second fields should line up vertically.
/// </summary>
public class GpsCoordinateFormatTests
{
    [Fact]
    public void MinutesAndSecondsAreRightAligned()
    {
        var (latitude, longitude) = GpsCoordinateFormat.Align("50° 6' 27,12\"", "14° 20' 7,54\"");

        Assert.Equal("50°  6' 27,12\"", latitude);
        Assert.Equal("14° 20'  7,54\"", longitude);
        AssertColumnsLineUp(latitude, longitude);
    }

    [Fact]
    public void DegreesWidenOnlyWhenThePairNeedsIt()
    {
        // Tokyo: a three-digit longitude pulls the two-digit latitude across to match.
        var (latitude, longitude) = GpsCoordinateFormat.Align("35° 41' 22,20\"", "139° 41' 30,12\"");

        Assert.Equal(" 35° 41' 22,20\"", latitude);
        Assert.Equal("139° 41' 30,12\"", longitude);
        AssertColumnsLineUp(latitude, longitude);
    }

    [Fact]
    public void ANegativeSignCountsTowardTheFieldWidth()
    {
        // Sydney: the southern latitude's sign occupies a column of its own.
        var (latitude, longitude) = GpsCoordinateFormat.Align("-33° 51' 24,00\"", "151° 12' 36,00\"");

        Assert.Equal("-33° 51' 24,00\"", latitude);
        Assert.Equal("151° 12' 36,00\"", longitude);
        AssertColumnsLineUp(latitude, longitude);
    }

    [Fact]
    public void SingleDigitDegreesAlignToo()
    {
        var (latitude, longitude) = GpsCoordinateFormat.Align("9° 1' 2,00\"", "80° 15' 3,50\"");

        Assert.Equal(" 9°  1'  2,00\"", latitude);
        Assert.Equal("80° 15'  3,50\"", longitude);
        AssertColumnsLineUp(latitude, longitude);
    }

    [Fact]
    public void TheDecimalSeparatorIsWhateverTheLibraryProduced()
    {
        // Formatting follows the current culture; alignment must not quietly change it.
        var (latitude, _) = GpsCoordinateFormat.Align("50° 6' 27.12\"", "14° 20' 7.54\"");

        Assert.Equal("50°  6' 27.12\"", latitude);
    }

    [Theory]
    [InlineData("")]
    [InlineData("50 degrees north")]
    [InlineData("50° 6'")]
    public void UnrecognizedValuesPassThroughUntouched(string value)
    {
        var (latitude, longitude) = GpsCoordinateFormat.Align(value, "14° 20' 7,54\"");

        Assert.Equal(value, latitude);

        // The other half is still worth aligning on its own terms.
        Assert.Equal("14° 20'  7,54\"", longitude);
    }

    [Fact]
    public void OneCoordinateWithoutTheOtherIsStillPadded()
    {
        var (latitude, longitude) = GpsCoordinateFormat.Align("50° 6' 7,12\"", string.Empty);

        Assert.Equal("50°  6'  7,12\"", latitude);
        Assert.Equal(string.Empty, longitude);
    }

    private static void AssertColumnsLineUp(string latitude, string longitude)
    {
        Assert.Equal(latitude.IndexOf('°'), longitude.IndexOf('°'));
        Assert.Equal(latitude.IndexOf('\''), longitude.IndexOf('\''));
        Assert.Equal(latitude.IndexOf(','), longitude.IndexOf(','));
    }
}