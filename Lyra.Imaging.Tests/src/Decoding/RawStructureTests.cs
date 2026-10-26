using Lyra.Imaging.Decoding.Structure;
using Lyra.Metadata.Exif;
using Lyra.Metadata.Tests.Support;
using Lyra.Metadata.Tiff;
using Xunit;
using static Lyra.Metadata.Tests.Support.TiffFixture;

namespace Lyra.Imaging.Tests.Decoding;

/// <summary>The structure view walks the directories the engine reads, maker notes included.</summary>
public class RawStructureTests
{
    private static byte[] NikonNote()
    {
        var tiff = Build(little: false, big: false, [Short(0x0002, 0, 200), Ascii(0x0001, "0210")]);
        return [.. "Nikon\0"u8, 2, 0x10, 0, 0, .. tiff];
    }

    private static byte[] File() => Build(
        [
            Ascii(ExifTags.Make, "NIKON CORPORATION"),
            Pointer(TiffDirectoryReader.TagExifIfd, Short(ExifTags.IsoEquivalent, 200), Undefined(ExifTags.MakerNote, NikonNote()), Pointer(TiffDirectoryReader.TagInteropIfd, Ascii(1, "R98"))),
            Pointer(TiffDirectoryReader.TagGpsIfd, Ascii(ExifTags.GpsLatitudeRef, "N"))
        ],
        [Short(ExifTags.Orientation, 1)]);

    [Fact]
    public void DirectoriesNestUnderWhatPointsAtThem()
    {
        var groups = RawStructure.Describe(new MemoryStream(File()))!;
        var depths = groups.ToDictionary(group => group.Name, group => group.Depth);

        Assert.Equal(7, groups.Count);
        Assert.Equal(0, depths["Image (IFD0)"]);
        Assert.Equal(1, depths["EXIF"]);
        Assert.Equal(1, depths["GPS"]);
        Assert.Equal(2, depths["Interoperability"]);
        Assert.Equal(2, depths["NIKON CORPORATION maker notes"]);
        Assert.Equal(0, depths["IFD1"]);
    }

    [Fact]
    public void AMakerNoteIsReadWithItsOwnByteOrderAndNames()
    {
        var notes = RawStructure.Describe(new MemoryStream(File()))!.Single(group => group.Name.EndsWith("maker notes"));
        var fields = notes.Fields.ToDictionary(field => field.Key, field => field.Value);

        Assert.Equal("Motorola, big-endian", fields["Byte order"]);
        Assert.Equal("0 200", fields["ISO"]);
        Assert.Equal("0210", fields["Firmware Version"]);
        Assert.Contains("Offsets count from", fields.Keys);
    }
}