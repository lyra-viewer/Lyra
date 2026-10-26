using Lyra.Metadata.Exif;
using Lyra.Metadata.Tiff;
using Xunit;
using static Lyra.Metadata.Tests.Support.TiffFixture;

namespace Lyra.Metadata.Tests.Tiff;

public class TiffDirectoryReaderTests
{
    public static TheoryData<bool, bool> Layouts => new()
    {
        { true, false },
        { false, false },
        { true, true },
        { false, true }
    };

    [Theory]
    [MemberData(nameof(Layouts))]
    public void TagsAreReadInEitherByteOrderFromTiffAndBigTiff(bool little, bool big)
    {
        var file = Build(little, big,
        [
            Ascii(ExifTags.Make, "ACME"),
            Short(ExifTags.Orientation, 6),
            Rational(0x011A, R(300))
        ]);

        var exif = ExifMetadata.Read(new MemoryStream(file));

        Assert.NotNull(exif);
        Assert.Equal("ACME", exif.Get(TiffDirectoryKind.Image, ExifTags.Make)?.Text);
        Assert.True(exif.TryGetInt32(TiffDirectoryKind.Image, ExifTags.Orientation, out var orientation));
        Assert.Equal(6, orientation);
        Assert.Equal(new TiffRational(300, 1), exif.Get(TiffDirectoryKind.Image, 0x011A)?.Rationals?[0]);
    }

    [Theory]
    [MemberData(nameof(Layouts))]
    public void PointerDirectoriesNestUnderTheirOwnerWithTheirKind(bool little, bool big)
    {
        var file = Build(little, big,
        [
            Ascii(ExifTags.Make, "ACME"),
            SubIfds([Long(254, 0), Long(256, 64)]),
            Pointer(TiffDirectoryReader.TagExifIfd, Rational(ExifTags.ExposureTime, R(1, 250)), Pointer(TiffDirectoryReader.TagInteropIfd, Ascii(ExifTags.InteropIndex, "R98"))),
            Pointer(TiffDirectoryReader.TagGpsIfd, Ascii(ExifTags.GpsLatitudeRef, "N"))
        ]);

        var layout = TiffDirectoryReader.Read(new MemoryStream(file));

        Assert.NotNull(layout);
        Assert.Equal(big, layout.BigTiff);

        var ifd0 = Assert.Single(layout.Directories);
        Assert.Equal([TiffDirectoryKind.SubImage, TiffDirectoryKind.Exif, TiffDirectoryKind.Gps], ifd0.Children.Select(child => child.Kind));
        Assert.Equal(TiffDirectoryKind.Interop, ifd0.Children.Single(child => child.Kind == TiffDirectoryKind.Exif).Children.Single().Kind);
    }

    [Fact]
    public void TheExifDirectoryIsNotMistakenForAnImageSubIfdListedBeforeIt()
    {
        // A NEF or DNG: the raw data's SubIFD (330) sorts before the EXIF pointer (0x8769).
        var file = Build(
        [
            SubIfds([Long(254, 0), Long(256, 6000)], [Long(254, 1), Long(513, 0)]),
            Pointer(TiffDirectoryReader.TagExifIfd, Rational(ExifTags.ExposureTime, R(1, 250)))
        ]);

        var exif = ExifMetadata.Read(new MemoryStream(file));

        Assert.Equal("1/250 sec", exif?.Describe(TiffDirectoryKind.Exif, ExifTags.ExposureTime));
    }

    [Fact]
    public void IfdZerosOwnExifWinsOverASubIfdsAndASubIfdsStandsInWithoutIt()
    {
        var subIfdExif = Pointer(TiffDirectoryReader.TagExifIfd, Rational(ExifTags.ExposureTime, R(1, 8)));
        var ownExif = Pointer(TiffDirectoryReader.TagExifIfd, Rational(ExifTags.ExposureTime, R(1, 250)));

        var both = ExifMetadata.Read(new MemoryStream(Build([SubIfds([Long(254, 0), subIfdExif]), ownExif])));
        var onlySubIfd = ExifMetadata.Read(new MemoryStream(Build([SubIfds([Long(254, 0), subIfdExif])])));

        Assert.Equal("1/250 sec", both?.Describe(TiffDirectoryKind.Exif, ExifTags.ExposureTime));
        Assert.Equal("1/8 sec", onlySubIfd?.Describe(TiffDirectoryKind.Exif, ExifTags.ExposureTime));
    }

    [Fact]
    public void ABigTiffExifPointerMayBeAnEightByteOffset()
    {
        var file = Build(little: true, big: true,
        [
            Pointer(TiffDirectoryReader.TagExifIfd, Short(ExifTags.IsoEquivalent, 400))
        ]);

        var exif = ExifMetadata.Read(new MemoryStream(file));

        Assert.Equal("400", exif?.Describe(TiffDirectoryKind.Exif, ExifTags.IsoEquivalent));
    }

    [Fact]
    public void AStreamThatIsNotATiffIsNotOne()
    {
        Assert.Null(TiffDirectoryReader.Read(new MemoryStream("\x89PNG\r\n\x1a\n........"u8.ToArray())));
        Assert.Null(TiffDirectoryReader.Read(new MemoryStream("II"u8.ToArray())));
        Assert.Null(ExifMetadata.Read(new MemoryStream([])));
    }

    [Fact]
    public void AChainThatLoopsBackEndsInsteadOfRepeating()
    {
        var file = Build([Short(ExifTags.Orientation, 1)], [Short(ExifTags.Orientation, 3)]);

        // Point IFD1's next-IFD field back at IFD0.
        var layout = TiffDirectoryReader.Read(new MemoryStream(file))!;
        var ifd1 = layout.Directories[1];
        var nextField = (int)ifd1.Offset + 2 + ifd1.Entries.Count * 12;
        BitConverter.GetBytes((uint)layout.Directories[0].Offset).CopyTo(file, nextField);

        var looped = TiffDirectoryReader.Read(new MemoryStream(file));

        Assert.Equal(2, looped?.Directories.Count);
    }

    [Fact]
    public void ADirectoryThatPointsAtItselfIsReadOnce()
    {
        var file = Build([Short(ExifTags.Orientation, 1), Pointer(TiffDirectoryReader.TagExifIfd, Short(ExifTags.IsoEquivalent, 100))]);

        var layout = TiffDirectoryReader.Read(new MemoryStream(file))!;
        var exifEntry = layout.Directories[0].Find(TiffDirectoryReader.TagExifIfd)!.Value;
        BitConverter.GetBytes((uint)layout.Directories[0].Offset).CopyTo(file, (int)exifEntry.ValueOffset);

        var selfPointing = TiffDirectoryReader.Read(new MemoryStream(file));

        Assert.Empty(selfPointing!.Directories[0].Children);
    }

    [Fact]
    public void AnAbsurdEntryCountIsRefusedRatherThanRead()
    {
        var file = Build([Short(ExifTags.Orientation, 1)]);
        var layout = TiffDirectoryReader.Read(new MemoryStream(file))!;
        BitConverter.GetBytes((ushort)65535).CopyTo(file, (int)layout.Directories[0].Offset);

        Assert.Null(TiffDirectoryReader.Read(new MemoryStream(file)));
    }

    [Fact]
    public void ADirectoryCutOffBeforeItsNextPointerKeepsItsEntries()
    {
        var file = Build([Short(ExifTags.Orientation, 6), Short(ExifTags.IsoEquivalent, 200)]);
        var ifd = TiffDirectoryReader.Read(new MemoryStream(file))!.Directories[0];
        var cut = file[..(int)(ifd.Offset + 2 + 2 * 12 + 2)];

        var layout = TiffDirectoryReader.Read(new MemoryStream(cut))!;

        Assert.Equal([ExifTags.Orientation, ExifTags.IsoEquivalent], layout.Directories.Single().Entries.Select(entry => entry.Tag));
        Assert.Null(TiffDirectoryReader.Read(new MemoryStream(cut[..^3])));
    }

    [Fact]
    public void AValuePointingPastTheEndIsLeftOut()
    {
        var file = Build([Ascii(ExifTags.Make, "A make long enough to live outside the entry"), Short(ExifTags.Orientation, 8)]);
        var layout = TiffDirectoryReader.Read(new MemoryStream(file))!;
        var make = layout.Directories[0].Find(ExifTags.Make)!.Value;

        // The make's offset field sits 8 bytes into its 12-byte entry.
        var entryAt = (int)layout.Directories[0].Offset + 2 + layout.Directories[0].Entries.ToList().IndexOf(make) * 12;
        BitConverter.GetBytes(0x7FFF0000u).CopyTo(file, entryAt + 8);

        var exif = ExifMetadata.Read(new MemoryStream(file));

        Assert.Null(exif?.Get(TiffDirectoryKind.Image, ExifTags.Make));
        Assert.Equal("8", exif?.Get(TiffDirectoryKind.Image, ExifTags.Orientation)?.Integers?[0].ToString());
    }

    [Fact]
    public void TextStopsAtItsFirstNulAndFallsBackToLatin1()
    {
        var file = Build(
        [
            new Entry(ExifTags.Make, 2, 9, _ => "Make\0Junk"u8.ToArray()),
            new Entry(ExifTags.Artist, 2, 6, _ => [0x4A, 0xF6, 0x72, 0x67, 0x00, 0x00])
        ]);

        var exif = ExifMetadata.Read(new MemoryStream(file));

        Assert.Equal("Make", exif?.Get(TiffDirectoryKind.Image, ExifTags.Make)?.Text);
        Assert.Equal("Jörg", exif?.Get(TiffDirectoryKind.Image, ExifTags.Artist)?.Text);
    }

    [Fact]
    public void ADamagedFileNeverThrowsOrHangs()
    {
        var file = Build(
        [
            Ascii(ExifTags.Make, "ACME Corporation"),
            Short(ExifTags.Orientation, 6),
            SubIfds([Long(254, 0), Long(256, 64), Long(273, 8)]),
            Pointer(
                TiffDirectoryReader.TagExifIfd,
                Rational(ExifTags.ExposureTime, R(1, 250)), Rational(ExifTags.FNumber, R(28, 10)), Short(ExifTags.Flash, 16),
                Ascii(ExifTags.DateTimeOriginal, "2024:09:26 18:32:17"), SignedRational(ExifTags.ExposureBias, R(-1, 3)),
                Pointer(TiffDirectoryReader.TagInteropIfd, Ascii(ExifTags.InteropIndex, "R98"))
            ),
            Pointer(
                TiffDirectoryReader.TagGpsIfd,
                Ascii(ExifTags.GpsLatitudeRef, "N"), Rational(ExifTags.GpsLatitude, R(40), R(26), R(463, 10)),
                Ascii(ExifTags.GpsLongitudeRef, "W"), Rational(ExifTags.GpsLongitude, R(79), R(58), R(56))
            )
        ]);

        var random = new Random(1234);
        for (var variant = 0; variant < 200; variant++)
        {
            var damaged = variant % 4 == 0
                ? file[..random.Next(0, file.Length)]
                : Flip(file, random, flips: 1 + variant % 8);

            var exif = ExifMetadata.Read(new MemoryStream(damaged));
            if (exif is null)
                continue;

            foreach (var kind in ExifMetadata.Kinds)
            foreach (var tag in exif.Directory(kind).Keys)
                _ = exif.Describe(kind, tag);
        }
    }

    private static byte[] Flip(byte[] file, Random random, int flips)
    {
        var copy = (byte[])file.Clone();
        for (var i = 0; i < flips; i++)
            copy[random.Next(copy.Length)] ^= (byte)(1 << random.Next(8));

        return copy;
    }
}