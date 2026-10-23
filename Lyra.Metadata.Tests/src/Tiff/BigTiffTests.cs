using System.Buffers.Binary;
using Lyra.Metadata.Exif;
using Lyra.Metadata.Tiff;
using Xunit;
using static Lyra.Metadata.Tests.Support.TiffFixture;

namespace Lyra.Metadata.Tests.Tiff;

/// <summary>
/// BigTIFF differs from TIFF in three places: version 43 rather than 42, 8-byte offsets, and
/// 20-byte entries with a 64-bit count.
/// </summary>
public class BigTiffTests
{
    private const int HeaderLength = 16;
    private const int EntryLength = 20;

    [Fact]
    public void AnOrdinaryTiffIsNotABigTiff()
    {
        Assert.False(TiffDirectoryReader.IsBigTiff(new MemoryStream(Build([Short(ExifTags.Orientation, 1)]))));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ABigTiffIsRecognisedInEitherByteOrder(bool little)
    {
        Assert.True(TiffDirectoryReader.IsBigTiff(new MemoryStream(Build(little, big: true, [Short(ExifTags.Orientation, 1)]))));
    }

    [Fact]
    public void RecognisingLeavesThePositionWhereItWas()
    {
        var stream = new MemoryStream(Build(little: true, big: true, [Short(ExifTags.Orientation, 1)])) { Position = 5 };

        TiffDirectoryReader.IsBigTiff(stream);

        Assert.Equal(5, stream.Position);
    }

    [Fact]
    public void AValueBeyondFourGigabytesIsStillFound()
    {
        const long farOffset = 0x1_0000_0008;

        var value = "FAR AWAY, PAST FOUR GIGABYTES\0"u8.ToArray();
        var head = Build(little: true, big: true, [Ascii(ExifTags.Make, new string('x', value.Length - 1))]);
        var make = EntryAt(head, 0);

        BinaryPrimitives.WriteInt64LittleEndian(head.AsSpan(make + 12), farOffset);

        using var stream = new SparseStream(head, farOffset, value);
        var exif = ExifMetadata.Read(stream);

        Assert.Equal("FAR AWAY, PAST FOUR GIGABYTES", exif?.Get(TiffDirectoryKind.Image, ExifTags.Make)?.Text);
    }

    [Fact]
    public void ARepeatedValueIsReadWhole()
    {
        var exif = ExifMetadata.Read(new MemoryStream(Build(little: true, big: true, [Short(0x0102, 8, 8, 8), Short(ExifTags.Orientation, 3)])));

        Assert.Equal([8L, 8, 8], exif?.Get(TiffDirectoryKind.Image, 0x0102)?.Integers ?? []);
        Assert.True(exif!.TryGetInt32(TiffDirectoryKind.Image, ExifTags.Orientation, out var orientation));
        Assert.Equal(3, orientation);
    }

    [Theory]
    [InlineData((ushort)4)]
    [InlineData((ushort)13)]
    [InlineData((ushort)16)]
    [InlineData((ushort)18)]
    public void TheExifDirectoryIsFollowedWhateverThePointerWidth(ushort pointerType)
    {
        var file = Build(little: true, big: true, [Pointer(TiffDirectoryReader.TagExifIfd, pointerType, Rational(ExifTags.ExposureTime, R(1, 250)))]);

        var exif = ExifMetadata.Read(new MemoryStream(file));

        Assert.Equal(new TiffRational(1, 250), exif?.Get(TiffDirectoryKind.Exif, ExifTags.ExposureTime)?.Rationals?[0]);
    }

    [Fact]
    public void PerStripTagsAreNotRead()
    {
        var offsets = Enumerable.Range(0, 64).Select(i => (ushort)i).ToArray();
        var exif = ExifMetadata.Read(new MemoryStream(Build(little: true, big: true, [Short(ExifTags.Orientation, 1), Short(273, offsets)])));

        Assert.Null(exif?.Get(TiffDirectoryKind.Image, 273));
        Assert.NotNull(exif?.Get(TiffDirectoryKind.Image, ExifTags.Orientation));
    }

    [Fact]
    public void ATruncatedFileYieldsNothingRatherThanThrowing()
    {
        var full = Build(little: true, big: true, [Ascii(ExifTags.Make, "a long value stored out of line")]);

        for (var length = 0; length < full.Length; length++)
            _ = ExifMetadata.Read(new MemoryStream(full[..length]));
    }

    [Fact]
    public void AnAbsurdEntryCountIsRefused()
    {
        var file = Build(little: true, big: true, [Short(ExifTags.Orientation, 1)]);
        BinaryPrimitives.WriteUInt64LittleEndian(file.AsSpan((int)Ifd0Offset(file)), ulong.MaxValue);

        Assert.Null(TiffDirectoryReader.Read(new MemoryStream(file)));
    }

    [Fact]
    public void AnOffsetPastTheEndIsSkipped()
    {
        var file = Build(little: true, big: true, [Ascii(ExifTags.Make, "a long value stored out of line"), Short(ExifTags.Orientation, 1)]);
        BinaryPrimitives.WriteInt64LittleEndian(file.AsSpan(EntryAt(file, 0) + 12), long.MaxValue);

        var exif = ExifMetadata.Read(new MemoryStream(file));

        Assert.Null(exif?.Get(TiffDirectoryKind.Image, ExifTags.Make));
        Assert.NotNull(exif?.Get(TiffDirectoryKind.Image, ExifTags.Orientation));
    }

    [Fact]
    public void ACountThatWouldOverflowIsRefused()
    {
        var file = Build(little: true, big: true, [Ascii(ExifTags.Make, "a long value stored out of line"), Short(ExifTags.Orientation, 1)]);
        BinaryPrimitives.WriteUInt64LittleEndian(file.AsSpan(EntryAt(file, 0) + 4), ulong.MaxValue);

        var layout = TiffDirectoryReader.Read(new MemoryStream(file));

        Assert.Null(layout?.Directories[0].Find(ExifTags.Make));
    }

    [Fact]
    public void AnUnknownFieldTypeIsSkipped()
    {
        var file = Build(little: true, big: true, [Short(ExifTags.Orientation, 1), Short(0x0131, 2)]);
        BinaryPrimitives.WriteUInt16LittleEndian(file.AsSpan(EntryAt(file, 0) + 2), 999);

        var layout = TiffDirectoryReader.Read(new MemoryStream(file));

        Assert.Null(layout?.Directories[0].Find(ExifTags.Orientation));
        Assert.NotNull(layout?.Directories[0].Find(0x0131));
    }

    [Fact]
    public void AnUnexpectedOffsetWidthIsRefused()
    {
        var file = Build(little: true, big: true, [Short(ExifTags.Orientation, 1)]);
        BinaryPrimitives.WriteUInt16LittleEndian(file.AsSpan(4), 16);

        Assert.Null(TiffDirectoryReader.Read(new MemoryStream(file)));
    }

    [Fact]
    public void ADirectoryPointingAtItselfDoesNotHang()
    {
        var file = Build(little: true, big: true, [Short(ExifTags.Orientation, 1), Pointer(TiffDirectoryReader.TagExifIfd, Short(ExifTags.IsoEquivalent, 100))]);

        var exifPointer = EntryAt(file, 1);
        BinaryPrimitives.WriteInt64LittleEndian(file.AsSpan(exifPointer + 12), Ifd0Offset(file));

        var layout = TiffDirectoryReader.Read(new MemoryStream(file));

        Assert.Empty(layout!.Directories[0].Children);
    }

    private static long Ifd0Offset(byte[] file) => BinaryPrimitives.ReadInt64LittleEndian(file.AsSpan(8));

    /// <summary>Where IFD0's entry at <paramref name="index"/> starts, entries being sorted by tag.</summary>
    private static int EntryAt(byte[] file, int index) => (int)Ifd0Offset(file) + 8 + index * EntryLength;

    /// <summary>A head, a hole of zeros, and a tail far out - a huge file without the disk space.</summary>
    private sealed class SparseStream(byte[] head, long tailOffset, byte[] tail) : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => true;
        public override bool CanWrite => false;
        public override long Length => tailOffset + tail.Length;
        public override long Position { get; set; }

        public override int Read(byte[] buffer, int offset, int count)
        {
            var read = 0;
            for (; read < count && Position < Length; read++, Position++)
            {
                buffer[offset + read] = Position < head.Length ? head[Position]
                    : Position >= tailOffset ? tail[Position - tailOffset]
                    : (byte)0;
            }

            return read;
        }

        public override long Seek(long offset, SeekOrigin origin) => Position = origin switch
        {
            SeekOrigin.Begin => offset,
            SeekOrigin.Current => Position + offset,
            _ => Length + offset
        };

        public override void Flush() { }
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}