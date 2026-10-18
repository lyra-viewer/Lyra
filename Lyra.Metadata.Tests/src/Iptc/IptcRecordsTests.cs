using Lyra.Metadata.Iptc;
using Xunit;
using static Lyra.Metadata.Tests.Support.BlockFixture;

namespace Lyra.Metadata.Tests.Iptc;

public class IptcRecordsTests
{
    [Fact]
    public void DataSetsAreReadInFileOrderWithRepeats()
    {
        var iptc = IptcRecords.Parse([.. Text(5, "Title"), .. Text(25, "leaf"), .. Text(25, "nature"), .. Text(80, "Ann")])!;

        Assert.Equal(["Title"], iptc.Get(2, IptcRecords.ObjectName));
        Assert.Equal(["leaf", "nature"], iptc.Get(2, IptcRecords.Keywords));
        Assert.Equal(["Ann"], iptc.Get(2, IptcRecords.ByLine));
        Assert.Empty(iptc.Get(2, IptcRecords.Caption));
    }

    [Fact]
    public void WithoutADeclaredSetEachValueIsUtf8OrElseLatin1()
    {
        var iptc = IptcRecords.Parse([.. Text(5, "Jörg"), .. DataSet(2, 120, [0x43, 0x61, 0x66, 0xE9])])!;

        Assert.Equal("Jörg", iptc.Get(2, IptcRecords.ObjectName)[0]);
        Assert.Equal("Café", iptc.Get(2, IptcRecords.Caption)[0]);
    }

    [Fact]
    public void ADeclaredSetAppliesFromThereOn()
    {
        var latin = IptcRecords.Parse([.. Text(120, "Jörg"), .. DeclareLatin1, .. Text(80, "Jörg")])!;
        var utf8 = IptcRecords.Parse([.. DeclareUtf8, .. DataSet(2, 120, [0x43, 0xE9])])!;

        Assert.Equal("Jörg", latin.Get(2, IptcRecords.Caption)[0]);
        Assert.Equal("JÃ¶rg", latin.Get(2, IptcRecords.ByLine)[0]);
        Assert.Equal("C�", utf8.Get(2, IptcRecords.Caption)[0]);
    }

    [Fact]
    public void AnExtendedLengthIsFollowed()
    {
        byte[] extended = [0x1C, 2, 120, 0x80, 0x04, 0, 0, 0, 5, .. "Hello"u8];
        var iptc = IptcRecords.Parse([.. extended, .. Text(5, "T")])!;

        Assert.Equal(["Hello"], iptc.Get(2, IptcRecords.Caption));
        Assert.Equal(["T"], iptc.Get(2, IptcRecords.ObjectName));
    }

    [Fact]
    public void ReadingStopsAtTheFirstThingThatIsNotADataSetAndKeepsWhatCameBefore()
    {
        var badMarker = IptcRecords.Parse([.. Text(5, "T1"), 0, 0, .. Text(105, "H")])!;
        var truncated = IptcRecords.Parse([.. Text(5, "T1"), 0x1C, 2, 105, 0, 50, 65])!;

        Assert.Equal(["T1"], badMarker.Get(2, IptcRecords.ObjectName));
        Assert.Empty(badMarker.Get(2, IptcRecords.Headline));
        Assert.Equal(["T1"], truncated.Get(2, IptcRecords.ObjectName));
        Assert.Empty(truncated.Get(2, IptcRecords.Headline));
    }

    [Fact]
    public void ABlockThatDoesNotStartWithADataSetIsNotOne()
    {
        Assert.Null(IptcRecords.Parse([]));
        Assert.Null(IptcRecords.Parse("8BIM"u8));
    }

    [Fact]
    public void DamagedBlocksNeverThrow()
    {
        byte[] block = [.. DeclareUtf8, .. Text(5, "Title"), .. Text(25, "a"), .. Text(25, "b"), .. Text(120, "Caption text")];
        var random = new Random(5);

        for (var variant = 0; variant < 200; variant++)
        {
            var damaged = variant % 3 == 0 ? block[..random.Next(block.Length)] : (byte[])block.Clone();
            if (variant % 3 != 0)
                for (var i = 0; i < 1 + variant % 5; i++)
                    damaged[random.Next(damaged.Length)] ^= (byte)(1 << random.Next(8));

            _ = IptcRecords.Parse(damaged)?.Get(2, IptcRecords.Keywords);
        }
    }
}