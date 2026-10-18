using Lyra.Metadata.Photoshop;
using Xunit;
using static Lyra.Metadata.Tests.Support.BlockFixture;

namespace Lyra.Metadata.Tests.Photoshop;

public class PhotoshopResourcesTests
{
    [Fact]
    public void ResourcesAreReadWithOddAndEvenNamesAndPadding()
    {
        var resources = PhotoshopResources.Parse(
        [
            .. Resource(0x03ED, [1, 2, 3]),
            .. Resource(PhotoshopResources.Iptc, [0x1C, 2, 5, 0, 1, (byte)'T'], name: "a"),
            .. Resource(PhotoshopResources.Xmp, "<x/>"u8.ToArray(), name: "ab", signature: "MeSa")
        ]);

        Assert.Equal(3, resources.Resources.Count);
        Assert.Equal([1, 2, 3], resources.Find(0x03ED));
        Assert.Equal("a", resources.Resources[1].Name);
        Assert.Equal("<x/>"u8.ToArray(), resources.Find(PhotoshopResources.Xmp));
        Assert.Null(resources.Find(PhotoshopResources.IccProfile));
    }

    [Fact]
    public void TheFirstOfTwoResourcesWithOneIdIsFound()
    {
        var resources = PhotoshopResources.Parse([.. Resource(PhotoshopResources.IccProfile, [1]), .. Resource(PhotoshopResources.IccProfile, [2])]);

        Assert.Equal([1], resources.Find(PhotoshopResources.IccProfile));
    }

    [Fact]
    public void ReadingStopsAtAnUnknownSignatureOrTruncationAndKeepsWhatCameBefore()
    {
        byte[] good = Resource(0x0404, [1, 2]);

        Assert.Single(PhotoshopResources.Parse([.. good, .. Resource(0x040F, [3], signature: "JUNK")]).Resources);
        Assert.Single(PhotoshopResources.Parse([.. good, .. Resource(0x040F, [3, 4, 5, 6])[..14]]).Resources);
        Assert.Empty(PhotoshopResources.Parse("not a resource block"u8).Resources);
    }

    [Fact]
    public void DamagedBlocksNeverThrow()
    {
        byte[] blocks = [.. Resource(0x0404, new byte[40], "name"), .. Resource(0x040F, new byte[133]), .. Resource(0x0424, new byte[9], "x")];
        var random = new Random(3);

        for (var variant = 0; variant < 200; variant++)
        {
            var damaged = variant % 3 == 0 ? blocks[..random.Next(blocks.Length)] : (byte[])blocks.Clone();
            if (variant % 3 != 0)
                for (var i = 0; i < 1 + variant % 5; i++)
                    damaged[random.Next(damaged.Length)] ^= (byte)(1 << random.Next(8));

            _ = PhotoshopResources.Parse(damaged).Find(0x0424);
        }
    }
}