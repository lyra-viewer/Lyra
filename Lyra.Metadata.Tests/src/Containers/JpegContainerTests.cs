using System.Buffers.Binary;
using System.Text;
using Lyra.Metadata.Containers;
using Lyra.Metadata.Exif;
using Lyra.Metadata.Photoshop;
using Xunit;
using Lyra.Metadata.Tests.Support;
using Blocks = Lyra.Metadata.Tests.Support.BlockFixture;

namespace Lyra.Metadata.Tests.Containers;

public class JpegContainerTests
{
    private static byte[] TiffWithMake(string make) => TiffFixture.Build([TiffFixture.Ascii(ExifTags.Make, make)]);

    private static string Text(MetadataBlock block) => Encoding.UTF8.GetString(block.Data);

    [Fact]
    public void BlocksComeInReadingOrderWhateverTheSegmentOrder()
    {
        byte[] resources =
        [
            .. Blocks.Resource(PhotoshopResources.Iptc, Blocks.Text(5, "Title")),
            .. Blocks.Resource(PhotoshopResources.Exif, TiffWithMake("FromPhotoshop")),
            .. Blocks.Resource(PhotoshopResources.Xmp, "<psxmp/>"u8.ToArray()),
            .. Blocks.Resource(PhotoshopResources.IccProfile, [9, 9])
        ];

        var jpeg = JpegFixture.Build([JpegFixture.Photoshop(resources), .. JpegFixture.Icc([1, 2, 3]), JpegFixture.Xmp("<app1/>"), JpegFixture.Exif(TiffWithMake("FromApp1")), JpegFixture.Frame()]);

        var container = JpegContainer.Read(jpeg)!;

        Assert.Equal(
            [MetadataBlockKind.Exif, MetadataBlockKind.Xmp, MetadataBlockKind.Icc, MetadataBlockKind.Iptc, MetadataBlockKind.Exif, MetadataBlockKind.Xmp, MetadataBlockKind.Icc],
            container.Blocks.Select(block => block.Kind)
        );
        Assert.Equal("<app1/>", Text(container.Blocks[1]));
        Assert.Equal([1, 2, 3], container.Blocks[2].Data);
        Assert.False(container.Damaged);
    }

    [Fact]
    public void IccChunksAreJoinedInFileOrder()
    {
        var profile = Enumerable.Range(0, 250).Select(i => (byte)i).ToArray();
        var chunks = JpegFixture.Icc(profile, chunkSize: 100);

        var inOrder = JpegContainer.Read(JpegFixture.Build([.. chunks, JpegFixture.Frame()]))!;
        var swapped = JpegContainer.Read(JpegFixture.Build(chunks[1], chunks[0], chunks[2], JpegFixture.Frame()))!;

        Assert.Equal(profile, inOrder.Blocks.Single().Data);
        Assert.Equal([.. profile[100..200], .. profile[..100], .. profile[200..]], swapped.Blocks.Single().Data);
    }

    [Fact]
    public void TheFirstFrameHeaderGivesTheFacts()
    {
        var container = JpegContainer.Read(JpegFixture.Build(JpegFixture.Frame(0xC2, 12, 1), JpegFixture.Frame(0xC0, 8, 3)))!;

        Assert.Equal(new JpegFacts(0xC2, 12, 1, 8, 8), container.Fact<JpegFacts>());
    }

    [Fact]
    public void SegmentsWithoutTheirSignatureAreNotMetadata()
    {
        var container = JpegContainer.Read(JpegFixture.Build(
            JpegFixture.Segment(0xE1, TiffWithMake("Bare")),
            JpegFixture.Segment(0xE1, [.. "XMP\0://ns.adobe.com/xap/1.0/\0"u8, .. "<x/>"u8]),
            JpegFixture.Segment(0xED, [.. "Adobe_Photoshop2.5:"u8, .. Blocks.Resource(PhotoshopResources.Iptc, Blocks.Text(5, "T"))]),
            JpegFixture.Frame()))!;

        Assert.Empty(container.Blocks);
    }

    [Fact]
    public void NothingAfterTheStartOfScanIsRead()
    {
        byte[] jpeg = [.. JpegFixture.Build(JpegFixture.Frame()), .. JpegFixture.Exif(TiffWithMake("After"))];

        Assert.Empty(JpegContainer.Read(jpeg)!.Blocks);
    }

    [Fact]
    public void ASegmentRunningOffTheEndKeepsWhatCameBeforeAndSaysSo()
    {
        byte[] jpeg = [0xFF, 0xD8, .. JpegFixture.Exif(TiffWithMake("Ok")), 0xFF, 0xE1, 0x7F, 0xFF, 1, 2];

        var container = JpegContainer.Read(jpeg)!;

        Assert.True(container.Damaged);
        Assert.Single(container.Blocks);
    }

    [Fact]
    public void GarbageWhereAMarkerShouldBeIsDamage()
    {
        byte[] jpeg = [0xFF, 0xD8, .. JpegFixture.Exif(TiffWithMake("Kept")), 0x12, 0x34, .. JpegFixture.Xmp("<lost/>"), .. JpegFixture.Frame()];

        var container = JpegContainer.Read(jpeg)!;

        Assert.True(container.Damaged);
        Assert.Equal([MetadataBlockKind.Exif], container.Blocks.Select(block => block.Kind));
    }

    [Fact]
    public void WhatIsNotAJpegIsNotOne()
    {
        Assert.Null(JpegContainer.Read([]));
        Assert.Null(JpegContainer.Read([0xFF, 0xD8, 0x12, 0x34]));
        Assert.Null(JpegContainer.Read("\x89PNG\r\n\x1a\n"u8));
    }

    [Fact]
    public void ARafIsReadThroughTheJpegItsHeaderPointsAt()
    {
        var jpeg = JpegFixture.Build(JpegFixture.Exif(TiffWithMake("FUJIFILM")), JpegFixture.Frame());
        var raf = new byte[148 + jpeg.Length + 100];
        "FUJIFILMCCD-RAW 0201FF383501"u8.CopyTo(raf);
        BinaryPrimitives.WriteUInt32BigEndian(raf.AsSpan(84), 148);
        BinaryPrimitives.WriteUInt32BigEndian(raf.AsSpan(88), (uint)jpeg.Length);
        jpeg.CopyTo(raf, 148);

        Assert.Single(RafContainer.Read(raf)!.Blocks);

        // Without a usable offset, the JPEG is looked for near the start.
        BinaryPrimitives.WriteUInt32BigEndian(raf.AsSpan(84), 0xFFFFFFF0);
        Assert.Single(RafContainer.Read(raf)!.Blocks);
    }

    [Fact]
    public void DamagedJpegsNeverThrow()
    {
        var jpeg = JpegFixture.Build([
            JpegFixture.Exif(TiffWithMake("Make")), JpegFixture.Xmp("<x:xmpmeta/>"), .. JpegFixture.Icc(new byte[300], 100),
            JpegFixture.Photoshop(Blocks.Resource(PhotoshopResources.Iptc, Blocks.Text(5, "Title"))), JpegFixture.Frame()
        ]);
        var random = new Random(21);

        for (var variant = 0; variant < 200; variant++)
        {
            var damaged = variant % 3 == 0 ? jpeg[..random.Next(jpeg.Length)] : (byte[])jpeg.Clone();
            if (variant % 3 != 0)
                for (var i = 0; i < 1 + variant % 6; i++)
                    damaged[random.Next(damaged.Length)] ^= (byte)(1 << random.Next(8));

            _ = JpegContainer.Read(damaged)?.Blocks.Count;
        }
    }

    [Fact]
    public void ARafHeaderSaysWhereItsPartsAre()
    {
        var head = new byte[RafContainer.HeaderLength];
        "FUJIFILMCCD-RAW 0201FF383501"u8.CopyTo(head);
        "X-T3"u8.CopyTo(head.AsSpan(28));
        "0100"u8.CopyTo(head.AsSpan(60));
        BinaryPrimitives.WriteUInt32BigEndian(head.AsSpan(84), 148);
        BinaryPrimitives.WriteUInt32BigEndian(head.AsSpan(88), 1000);
        BinaryPrimitives.WriteUInt32BigEndian(head.AsSpan(100), 2000);
        BinaryPrimitives.WriteUInt32BigEndian(head.AsSpan(104), 50);

        var raf = RafContainer.ReadHeader(head)!;

        Assert.Equal("X-T3", raf.Camera);
        Assert.Equal("0201", raf.FormatVersion);
        Assert.Equal(new RafBlock(148, 1000), raf.Jpeg);
        Assert.True(raf.Jpeg.FitsIn(1148));
        Assert.False(raf.Jpeg.FitsIn(1147));
        Assert.False(raf.Metadata.FitsIn(10_000));
        Assert.Null(RafContainer.ReadHeader(head[..100]));
    }

    [Fact]
    public void TheFrameHeaderGivesTheSize()
    {
        byte[] frame = [0xFF, 0xC3, 0, 11, 14, 0x0F, 0xA0, 0x17, 0x70, 1, 1, 0x11, 0];

        var facts = JpegContainer.Read([0xFF, 0xD8, .. frame])!.Fact<JpegFacts>()!;

        Assert.Equal(new JpegFacts(0xC3, 14, 1, 6000, 4000), facts);
        Assert.True(facts.Lossless);
    }
}