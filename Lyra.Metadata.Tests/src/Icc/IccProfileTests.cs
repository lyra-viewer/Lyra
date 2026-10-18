using System.Buffers.Binary;
using Lyra.Metadata.Icc;
using Xunit;
using static Lyra.Metadata.Tests.Support.IccFixture;

namespace Lyra.Metadata.Tests.Icc;

public class IccProfileTests
{
    [Fact]
    public void AVersion2DescriptionIsReadUpToItsCount()
    {
        var profile = IccProfile.Parse(Profile("RGB ", 2, Desc("sRGB IEC61966-2.1"), Text("cprt", "Copyright Lyra")))!;

        Assert.Equal("RGB ", profile.ColorSpace);
        Assert.Equal("sRGB IEC61966-2.1", profile.Description);
    }

    [Fact]
    public void AVersion2CountIsTrustedEvenWhenItIsWrong()
    {
        // The count includes the NUL; one short of the text drops its last character.
        Assert.Equal("sRGB", IccProfile.Parse(Profile("RGB ", 2, Desc("sRGB IEC61966-2.1", count: 5)))?.Description);
        Assert.Equal("Plai", IccProfile.Parse(Profile("RGB ", 2, Desc("Plain", count: 5)))?.Description);
    }

    [Fact]
    public void AVersion4DescriptionIsItsFirstRecord()
    {
        var profile = IccProfile.Parse(Profile("RGB ", 4, Mluc(["enUS", "Display P3"], ["deDE", "Anzeige P3"])))!;

        Assert.Equal("Display P3", profile.Description);
    }

    [Fact]
    public void AVersion4DescriptionSkipsARecordThatPointsOutsideTheTag()
    {
        var tag = Mluc(["enUS", "Broken"], ["deDE", "Anzeige P3"]);
        BinaryPrimitives.WriteUInt32BigEndian(tag.Data.AsSpan(24), uint.MaxValue);

        Assert.Equal("Anzeige P3", IccProfile.Parse(Profile("RGB ", 4, tag))?.Description);
    }

    [Fact]
    public void AVersion4NameEndsAtTheNulSomeWritersCount()
    {
        var profile = IccProfile.Parse(Profile("RGB ", 4, Mluc(["enUS", "sRGB IEC61966-2.1\0"])))!;

        Assert.Equal("sRGB IEC61966-2.1", profile.Description);
    }

    [Fact]
    public void ATextTypedDescriptionIsRead()
    {
        Assert.Equal("Text desc", IccProfile.Parse(Profile("RGB ", 2, Text("desc", "Text desc")))?.Description);
    }

    [Fact]
    public void AProfileWithoutADescriptionStillHasItsColorSpace()
    {
        var profile = IccProfile.Parse(Profile("CMYK", 2))!;

        Assert.Equal("CMYK", profile.ColorSpace);
        Assert.Null(profile.Description);
    }

    [Fact]
    public void ATagTableRunningOffTheEndKeepsTheHeader()
    {
        var full = Profile("GRAY", 2, Desc("Gray"));
        var cut = full[..140];

        var profile = IccProfile.Parse(cut)!;

        Assert.Equal("GRAY", profile.ColorSpace);
        Assert.Null(profile.Description);
    }

    [Fact]
    public void AnAbsurdTagCountIsBounded()
    {
        var profile = Profile("RGB ", 2, Desc("Real"));
        BinaryPrimitives.WriteUInt32BigEndian(profile.AsSpan(128), uint.MaxValue);

        Assert.Equal("Real", IccProfile.Parse(profile)?.Description);
    }

    [Fact]
    public void TooLittleToHoldAColorSpaceIsNoProfile()
    {
        Assert.Null(IccProfile.Parse(new byte[23]));
        Assert.Null(IccProfile.Parse([]));
    }

    [Fact]
    public void DamagedProfilesNeverThrow()
    {
        byte[][] profiles = [Profile("RGB ", 4, Mluc(["enUS", "Display P3"], ["deDE", "Anzeige P3"]), Text("cprt", "c")), Profile("GRAY", 2, Desc("Gray Gamma 2.2"))];
        var random = new Random(11);

        for (var variant = 0; variant < 200; variant++)
        {
            var source = profiles[variant % 2];
            var damaged = variant % 3 == 0 ? source[..random.Next(source.Length)] : (byte[])source.Clone();
            if (variant % 3 != 0)
                for (var i = 0; i < 1 + variant % 6; i++)
                    damaged[random.Next(damaged.Length)] ^= (byte)(1 << random.Next(8));

            _ = IccProfile.Parse(damaged)?.Description;
        }
    }
}