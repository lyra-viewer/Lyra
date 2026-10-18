using System.Text;
using Lyra.Metadata.Exif;
using Lyra.Metadata.MakerNotes;
using Lyra.Metadata.Tests.Support;
using Lyra.Metadata.Tiff;
using Xunit;

namespace Lyra.Metadata.Tests.MakerNotes;

public class MakerNoteTests
{
    private static byte[] Head(string prefix, params byte[] rest) => [.. Encoding.ASCII.GetBytes(prefix), .. rest, .. new byte[24]];

    [Fact]
    public void ANikonNoteIsATiffOfItsOwnAfterItsHeader()
    {
        var little = MakerNote.Locate(Head("Nikon\0", 2, 0x10, 0, 0, (byte)'I', (byte)'I', 42, 0, 8, 0, 0, 0), "NIKON CORPORATION", littleEndian: false);
        var big = MakerNote.Locate(Head("Nikon\0", 2, 0x10, 0, 0, (byte)'M', (byte)'M', 0, 42, 0, 0, 0, 8), "NIKON", littleEndian: true);

        Assert.Equal(new MakerNoteLayout(18, true, 10, MakerNoteVendor.Nikon), little);
        Assert.Equal(new MakerNoteLayout(18, false, 10, MakerNoteVendor.Nikon), big);
    }

    [Theory]
    [InlineData("OLYMPUS\0II", "OLYMPUS CORPORATION", 12, true, 0L, MakerNoteVendor.Olympus)]
    [InlineData("OM SYSTEM\0\0\0MM", "OM Digital Solutions", 16, false, 0L, MakerNoteVendor.Olympus)]
    [InlineData("Apple iOS\0", "Apple", 14, false, 0L, MakerNoteVendor.Apple)]
    [InlineData("PENTAX \0II", "RICOH IMAGING", 10, true, 0L, MakerNoteVendor.Pentax)]
    [InlineData("AOC\0MM", "Asahi", 6, false, null, MakerNoteVendor.Pentax)]
    [InlineData("Panasonic\0", "Panasonic", 12, true, null, MakerNoteVendor.Panasonic)]
    [InlineData("SONY DSC \0", "SONY", 12, true, null, MakerNoteVendor.Sony)]
    [InlineData("LEICA\0", "LEICA CAMERA AG", 8, true, null, MakerNoteVendor.Leica)]
    [InlineData("SIGMA\0", "SIGMA", 8, true, null, MakerNoteVendor.Sigma)]
    [InlineData("\x0B\0", "Canon", 0, true, null, MakerNoteVendor.Canon)]
    [InlineData("\x0B\0", "Hasselblad", 0, true, null, null)]
    public void HeadersPlaceTheDirectory(string prefix, string make, long directory, bool little, long? baseOffset, MakerNoteVendor? vendor)
    {
        Assert.Equal(new MakerNoteLayout(directory, little, baseOffset, vendor), MakerNote.Locate(Head(prefix), make, littleEndian: true));
    }

    [Fact]
    public void AFujifilmNoteSaysWhereItsDirectoryIs()
    {
        Assert.Equal(new MakerNoteLayout(12, true, 0, MakerNoteVendor.Fujifilm), MakerNote.Locate(Head("FUJIFILM", 12, 0, 0, 0), "FUJIFILM", littleEndian: false));
    }

    [Fact]
    public void AnAocNoteWithASpaceKeepsTheTiffsByteOrder()
    {
        Assert.True(MakerNote.Locate(Head("AOC\0  "), null, littleEndian: true).LittleEndian);
        Assert.False(MakerNote.Locate(Head("AOC\0  "), null, littleEndian: false).LittleEndian);
    }

    [Fact]
    public void ADirectoryIsBelievedOnlyWhenItsEntriesLookReal()
    {
        var tiff = TiffFixture.Build([TiffFixture.Short(ExifTags.Orientation, 1), TiffFixture.Ascii(ExifTags.Make, "A make")]);
        using var stream = new MemoryStream(tiff);
        var at = TiffDirectoryReader.Read(stream)!.Directories[0].Offset;

        Assert.True(MakerNote.LooksLikeDirectory(stream, at, littleEndian: true));
        Assert.False(MakerNote.LooksLikeDirectory(stream, at, littleEndian: false));
        Assert.False(MakerNote.LooksLikeDirectory(stream, tiff.Length - 1, littleEndian: true));
        Assert.False(MakerNote.LooksLikeDirectory(new MemoryStream(new byte[64]), 0, littleEndian: true));
    }

    [Fact]
    public void NamesAreTheVendorsOwn()
    {
        Assert.Equal("ISO", MakerNoteTags.Name(MakerNoteVendor.Nikon, 0x0002));
        Assert.Equal("Image Type", MakerNoteTags.Name(MakerNoteVendor.Canon, 0x0006));
        Assert.Null(MakerNoteTags.Name(MakerNoteVendor.Leica, 0x7777));
    }
}