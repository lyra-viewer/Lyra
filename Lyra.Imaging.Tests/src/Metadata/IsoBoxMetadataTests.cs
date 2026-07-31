using System.Buffers.Binary;
using System.Text;
using Lyra.Imaging.Content;
using Lyra.Imaging.Metadata;
using Lyra.Imaging.Tests.Support;
using Xunit;

namespace Lyra.Imaging.Tests.Metadata;

/// <summary>
/// Covers metadata extraction from the ISOBMFF-style containers MetadataExtractor cannot open:
/// JPEG XL and JPEG 2000. The containers are synthesized here - no codestream is needed, since
/// only the top-level boxes are walked.
/// </summary>
public class IsoBoxMetadataTests
{
    private static readonly byte[] JxlSignature = [0x00, 0x00, 0x00, 0x0C, 0x4A, 0x58, 0x4C, 0x20, 0x0D, 0x0A, 0x87, 0x0A];
    private static readonly byte[] Jp2Signature = [0x00, 0x00, 0x00, 0x0C, 0x6A, 0x50, 0x20, 0x20, 0x0D, 0x0A, 0x87, 0x0A];
    private static readonly byte[] ExifUuid = [0x4A, 0x70, 0x67, 0x54, 0x69, 0x66, 0x66, 0x45, 0x78, 0x69, 0x66, 0x2D, 0x3E, 0x4A, 0x50, 0x32];
    private static readonly byte[] XmpUuid = [0xBE, 0x7A, 0xCF, 0xCB, 0x97, 0xA9, 0x42, 0xE8, 0x9C, 0x71, 0x99, 0x94, 0x91, 0xE3, 0xAF, 0xAC];

    [Fact]
    public void Jxl_ReadsExifAndXmpBoxes()
    {
        var tiff = ExifJpegBuilder.BuildTiff(orientation: 6);
        var xmp = ExifJpegBuilder.BuildXmpPacket(new ExifJpegBuilder.XmpFields(Title: "From a JXL"));

        var container = Build(JxlSignature,
            Box("jxlc", [0xFF, 0x0A]),
            Box("Exif", [.. TiffOffsetPrefix, .. tiff]),
            Box("xml ", xmp));

        var payloads = IsoBoxMetadata.ReadJxl(container);

        Assert.NotNull(payloads.Exif);
        Assert.NotNull(payloads.Xmp);

        var info = MetadataProcessor.ParseMetadata(payloads.Exif, payloads.Xmp, "test.jxl");
        Assert.Equal(ExifOrientation.Rotate90Cw, info.OrientationValue);
        Assert.Contains(new ExifEntry("Title", "From a JXL"), info.ToKeyValuePairs());
    }

    [Fact]
    public void Jp2_ReadsExifAndXmpFromUuidBoxes()
    {
        var tiff = ExifJpegBuilder.BuildTiff(orientation: 8);
        var xmp = ExifJpegBuilder.BuildXmpPacket(new ExifJpegBuilder.XmpFields(Title: "From a JP2"));

        var container = Build(Jp2Signature,
            Box("ftyp", Encoding.ASCII.GetBytes("jp2 ")),
            Box("uuid", [.. ExifUuid, .. tiff]),
            Box("uuid", [.. XmpUuid, .. xmp]));

        var payloads = IsoBoxMetadata.ReadJp2(container);

        var info = MetadataProcessor.ParseMetadata(payloads.Exif, payloads.Xmp, "test.jp2");
        Assert.Equal(ExifOrientation.Rotate270Cw, info.OrientationValue);
        Assert.Contains(new ExifEntry("Title", "From a JP2"), info.ToKeyValuePairs());
    }

    [Fact]
    public void Jxl_AcceptsAnExifBoxWithoutTheOffsetPrefix()
    {
        // The prefix is required by the spec, but writers omit it; the TIFF header is located
        // rather than assumed.
        var container = Build(JxlSignature, Box("Exif", ExifJpegBuilder.BuildTiff(orientation: 3)));

        var payloads = IsoBoxMetadata.ReadJxl(container);

        Assert.Equal(ExifOrientation.Rotate180,
            MetadataProcessor.ParseMetadata(payloads.Exif, null, "test.jxl").OrientationValue);
    }

    [Fact]
    public void Jp2_IgnoresUuidBoxesItDoesNotRecognize()
    {
        var unknownUuid = new byte[16];
        var container = Build(Jp2Signature, Box("uuid", [.. unknownUuid, .. ExifJpegBuilder.BuildTiff()]));

        Assert.True(IsoBoxMetadata.ReadJp2(container).IsEmpty);
    }

    [Fact]
    public void BareCodestreamsCarryNoBoxesAndAreReportedEmpty()
    {
        Assert.True(IsoBoxMetadata.ReadJxl([0xFF, 0x0A, 0x00, 0x01]).IsEmpty);
        Assert.True(IsoBoxMetadata.ReadJp2([0xFF, 0x4F, 0xFF, 0x51]).IsEmpty);
    }

    [Fact]
    public void MalformedContainersEndTheWalkInsteadOfThrowing()
    {
        // A box claiming far more bytes than the file holds - the shape a truncated download has.
        var lying = new List<byte>(JxlSignature);
        lying.AddRange([0x7F, 0xFF, 0xFF, 0xFF]);
        lying.AddRange(Encoding.ASCII.GetBytes("Exif"));
        lying.AddRange([0x00, 0x00, 0x00, 0x00]);

        Assert.True(IsoBoxMetadata.ReadJxl(lying.ToArray()).IsEmpty);

        // Truncated mid-header.
        Assert.True(IsoBoxMetadata.ReadJxl([.. JxlSignature, 0x00, 0x00]).IsEmpty);

        // A box whose size field is smaller than its own header.
        Assert.True(IsoBoxMetadata.ReadJxl([.. JxlSignature, 0x00, 0x00, 0x00, 0x02, 0x45, 0x78, 0x69, 0x66]).IsEmpty);
    }

    [Fact]
    public void AnExifBoxWithoutATiffHeaderIsDiscarded()
    {
        var container = Build(JxlSignature, Box("Exif", Encoding.ASCII.GetBytes("not a tiff block at all")));

        Assert.True(IsoBoxMetadata.ReadJxl(container).IsEmpty);
    }

    private static readonly byte[] TiffOffsetPrefix = [0x00, 0x00, 0x00, 0x00];

    private static byte[] Box(string type, byte[] payload)
    {
        var box = new byte[8 + payload.Length];
        BinaryPrimitives.WriteUInt32BigEndian(box, (uint)box.Length);
        Encoding.ASCII.GetBytes(type).CopyTo(box, 4);
        payload.CopyTo(box, 8);
        return box;
    }

    private static byte[] Build(byte[] signature, params byte[][] boxes) => [.. signature, .. boxes.SelectMany(b => b)];
}
