using System.Buffers.Binary;
using System.Text;
using Lyra.Imaging.Content;
using Lyra.Imaging.Metadata;
using MetadataExtractor;
using MetadataExtractor.Formats.Exif;
using Xunit;
using Directory = MetadataExtractor.Directory;
using ExifReader = Lyra.Imaging.Metadata.ExifReader;

namespace Lyra.Imaging.Tests.Metadata;

public class BigTiffMetadataReaderTests
{
    private const ushort TypeAscii = 2;
    private const ushort TypeShort = 3;
    private const ushort TypeLong = 4;
    private const ushort TypeRational = 5;
    private const ushort TypeLong8 = 16;

    private const int TagMake = 0x010F;
    private const int TagModel = 0x0110;
    private const int TagOrientation = 0x0112;
    private const int TagXResolution = 0x011A;
    private const int TagStripOffsets = 273;
    private const int TagExifPointer = 0x8769;
    private const int TagExposureTime = 0x829A;

    [Fact]
    public void AnOrdinaryTiffIsLeftToMetadataExtractor()
    {
        // Version 42, the classic one, which MetadataExtractor reads perfectly well itself.
        var classic = new byte[] { (byte)'I', (byte)'I', 42, 0, 8, 0, 0, 0 };

        Assert.False(BigTiffMetadataReader.IsBigTiff(new MemoryStream(classic)));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ABigTiffIsRecognisedInEitherByteOrder(bool little)
    {
        Assert.True(BigTiffMetadataReader.IsBigTiff(new MemoryStream(Build(little, [Entry.Short(TagOrientation, 1)]))));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void TagsAreReadInEitherByteOrder(bool little)
    {
        var file = Build(little,
        [
            Entry.Ascii(TagMake, "ACME"),
            Entry.Short(TagOrientation, 6),
            Entry.Rational(TagXResolution, 300, 1)
        ]);

        var ifd0 = Ifd0(file);

        Assert.Equal("ACME", ifd0.GetString(TagMake));
        Assert.Equal(6, ifd0.GetInt32(TagOrientation));
        Assert.Equal(new Rational(300, 1), ifd0.GetRational(TagXResolution));
    }

    [Fact]
    public void AsciiLosesItsTerminator()
    {
        var ifd0 = Ifd0(Build(true, [Entry.Ascii(TagModel, "Scanner 9000")]));

        Assert.Equal("Scanner 9000", ifd0.GetString(TagModel));
    }
    
    [Fact]
    public void AValueBeyondFourGigabytesIsStillFound()
    {
        const ulong farOffset = 0x1_0000_0008;

        var value = Encoding.UTF8.GetBytes("FAR AWAY, PAST FOUR GIGABYTES\0");
        var head = Build(true, [Entry.Ascii(TagMake, new string('x', value.Length - 1))]);

        // Point the entry's value at the far offset instead of the heap right after the directory.
        BinaryPrimitives.WriteUInt64LittleEndian(head.AsSpan(16 + 8 + 12), farOffset);

        using var stream = new SparseStream(head, farOffset, value);
        var ifd0 = BigTiffMetadataReader.Read(stream).OfType<ExifIfd0Directory>().Single();

        Assert.Equal("FAR AWAY, PAST FOUR GIGABYTES", ifd0.GetString(TagMake));
    }

    [Fact]
    public void ARepeatedValueBecomesAnArray()
    {
        var ifd0 = Ifd0(Build(true, [Entry.Shorts(0x0102, [8, 8, 8])]));

        Assert.Equal([8, 8, 8], ifd0.GetInt32Array(0x0102));
    }

    [Fact]
    public void ALoneValueIsNotWrappedInAnArray()
    {
        var ifd0 = Ifd0(Build(true, [Entry.Short(TagOrientation, 3)]));

        Assert.Equal(3, ifd0.GetObject(TagOrientation));
    }
    
    [Theory]
    [InlineData(TypeLong)]
    [InlineData(TypeLong8)]
    public void TheExifSubDirectoryIsFollowed(ushort pointerType)
    {
        var file = BuildWithSubIfd(pointerType, [Entry.Rational(TagExposureTime, 1, 250)]);

        var sub = Read(file).OfType<ExifSubIfdDirectory>().FirstOrDefault();

        Assert.NotNull(sub);
        Assert.Equal(new Rational(1, 250), sub.GetRational(TagExposureTime));
    }

    [Fact]
    public void OrientationReachesTheRecordTheViewerReads()
    {
        var directories = Read(Build(true, [Entry.Short(TagOrientation, 8)]));

        var exif = ExifReader.Read(directories);

        Assert.Equal(ExifOrientation.Rotate270Cw, exif.OrientationValue);
    }
    
    [Fact]
    public void PerStripTagsAreNotRead()
    {
        var offsets = Enumerable.Range(0, 64).Select(i => (ushort)i).ToArray();

        var ifd0 = Ifd0(Build(true, [Entry.Short(TagOrientation, 1), Entry.Shorts(TagStripOffsets, offsets)]));

        Assert.Null(ifd0.GetObject(TagStripOffsets));
        Assert.Equal(1, ifd0.GetInt32(TagOrientation));
    }

    // ------------------------------------------------------------------
    //  Files that are wrong
    // ------------------------------------------------------------------
    
    [Fact]
    public void ATruncatedFileYieldsNothingRatherThanThrowing()
    {
        var full = Build(true, [Entry.Ascii(TagMake, "a long value stored out of line")]);

        for (var length = 0; length < full.Length; length++)
            Assert.NotNull(BigTiffMetadataReader.Read(new MemoryStream(full[..length])));
    }

    [Fact]
    public void AnAbsurdEntryCountIsRefused()
    {
        var file = Build(true, [Entry.Short(TagOrientation, 1)]);
        BinaryPrimitives.WriteUInt64LittleEndian(file.AsSpan(16), ulong.MaxValue);

        Assert.Empty(ReadOrFail(file));
    }

    [Fact]
    public void AnOffsetPastTheEndIsSkipped()
    {
        var file = Build(true, [Entry.Ascii(TagMake, "a long value stored out of line")]);

        // The value offset is the last eight bytes of the sole entry.
        BinaryPrimitives.WriteUInt64LittleEndian(file.AsSpan(16 + 8 + 12), long.MaxValue);

        var directories = ReadOrFail(file);
        Assert.DoesNotContain(directories.SelectMany(d => d.Tags), t => t.Type == TagMake);
    }
    
    [Fact]
    public void ACountThatWouldOverflowIsRefused()
    {
        var file = Build(true, [Entry.Ascii(TagMake, "a long value stored out of line")]);

        BinaryPrimitives.WriteUInt64LittleEndian(file.AsSpan(16 + 8 + 4), ulong.MaxValue);

        Assert.DoesNotContain(ReadOrFail(file).SelectMany(d => d.Tags), t => t.Type == TagMake);
    }

    [Fact]
    public void AnUnknownFieldTypeIsSkipped()
    {
        var file = Build(true, [Entry.Short(TagOrientation, 1)]);
        BinaryPrimitives.WriteUInt16LittleEndian(file.AsSpan(16 + 8 + 2), 999);

        Assert.Empty(ReadOrFail(file));
    }
    
    [Fact]
    public void AnUnexpectedOffsetWidthIsRefused()
    {
        var file = Build(true, [Entry.Short(TagOrientation, 1)]);
        BinaryPrimitives.WriteUInt16LittleEndian(file.AsSpan(4), 16);

        Assert.Empty(ReadOrFail(file));
    }

    [Fact]
    public void ADirectoryPointingAtItselfDoesNotHang()
    {
        var file = BuildWithSubIfd(TypeLong8, [Entry.Short(TagOrientation, 1)], subIfdPointsAtItself: true);

        Assert.NotNull(ReadOrFail(file));
    }

    // ------------------------------------------------------------------
    //  Building files
    // ------------------------------------------------------------------

    private static ExifIfd0Directory Ifd0(byte[] file)
    {
        var ifd0 = Read(file).OfType<ExifIfd0Directory>().FirstOrDefault();
        Assert.NotNull(ifd0);
        return ifd0;
    }

    private static IReadOnlyList<Directory> Read(byte[] file) => BigTiffMetadataReader.Read(new MemoryStream(file));

    private static IReadOnlyList<Directory> ReadOrFail(byte[] file)
    {
        var directories = BigTiffMetadataReader.Read(new MemoryStream(file));
        Assert.NotNull(directories);
        return directories;
    }

    private readonly record struct Entry(int Tag, ushort Type, ulong Count, byte[] Data)
    {
        public static Entry Ascii(int tag, string value)
        {
            var bytes = Encoding.UTF8.GetBytes(value + '\0');
            return new Entry(tag, TypeAscii, (ulong)bytes.Length, bytes);
        }

        public static Entry Short(int tag, ushort value) => Shorts(tag, [value]);

        public static Entry Shorts(int tag, ushort[] values)
        {
            var bytes = new byte[values.Length * 2];
            for (var i = 0; i < values.Length; i++)
                BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(i * 2), values[i]);

            return new Entry(tag, TypeShort, (ulong)values.Length, bytes);
        }

        public static Entry Rational(int tag, uint numerator, uint denominator)
        {
            var bytes = new byte[8];
            BinaryPrimitives.WriteUInt32LittleEndian(bytes, numerator);
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), denominator);

            return new Entry(tag, TypeRational, 1, bytes);
        }

        public static Entry Pointer(int tag, ushort type, ulong offset)
        {
            var bytes = new byte[type == TypeLong ? 4 : 8];
            if (type == TypeLong)
                BinaryPrimitives.WriteUInt32LittleEndian(bytes, (uint)offset);
            else
                BinaryPrimitives.WriteUInt64LittleEndian(bytes, offset);

            return new Entry(tag, type, 1, bytes);
        }
    }
    
    private static byte[] Build(bool little, Entry[] entries)
    {
        const long ifdOffset = 16L;
        var heapOffset = ifdOffset + 8 + entries.Length * 20L + 8;

        var heap = new List<byte>();
        var file = new byte[heapOffset];

        WriteHeader(file, little, ifdOffset);
        WriteIfd(file, little, ifdOffset, entries, (ulong)heapOffset, heap, nextIfd: 0);

        return [.. file, .. heap];
    }

    private static byte[] BuildWithSubIfd(ushort pointerType, Entry[] subEntries, bool subIfdPointsAtItself = false)
    {
        const long ifd0Offset = 16;
        const long ifd0Length = 8 + 20L + 8;
        const long subOffset = ifd0Offset + ifd0Length;
        var subLength = 8 + subEntries.Length * 20L + 8;
        var heapOffset = subOffset + subLength;

        var heap = new List<byte>();
        var file = new byte[heapOffset];

        WriteHeader(file, little: true, ifd0Offset);
        WriteIfd(file, true, ifd0Offset, [Entry.Pointer(TagExifPointer, pointerType, subOffset)], (ulong)heapOffset, heap, nextIfd: 0);
        WriteIfd(file, true, subOffset, subEntries, (ulong)(heapOffset + heap.Count), heap, nextIfd: subIfdPointsAtItself ? (ulong)subOffset : 0);

        return [.. file, .. heap];
    }

    private static void WriteHeader(byte[] file, bool little, ulong firstIfd)
    {
        file[0] = file[1] = (byte)(little ? 'I' : 'M');
        Write16(file.AsSpan(2), little, 43);
        Write16(file.AsSpan(4), little, 8);
        Write16(file.AsSpan(6), little, 0);
        Write64(file.AsSpan(8), little, firstIfd);
    }

    private static void WriteIfd(byte[] file, bool little, long offset, Entry[] entries, ulong heapStart, List<byte> heap, ulong nextIfd)
    {
        Write64(file.AsSpan((int)offset), little, (ulong)entries.Length);

        var cursor = (int)offset + 8;

        foreach (var entry in entries)
        {
            Write16(file.AsSpan(cursor), little, (ushort)entry.Tag);
            Write16(file.AsSpan(cursor + 2), little, entry.Type);
            Write64(file.AsSpan(cursor + 4), little, entry.Count);

            // The builder writes values little-endian; re-order them for a big-endian file.
            var data = little ? entry.Data : Reorder(entry);

            if (data.Length <= 8)
                data.CopyTo(file.AsSpan(cursor + 12));
            else
            {
                Write64(file.AsSpan(cursor + 12), little, heapStart + (ulong)heap.Count);
                heap.AddRange(data);
            }

            cursor += 20;
        }

        Write64(file.AsSpan(cursor), little, nextIfd);
    }

    /// <summary>Flips each value of an entry end for end, one unit at a time.</summary>
    private static byte[] Reorder(Entry entry)
    {
        var unit = entry.Type switch
        {
            TypeShort => 2,
            TypeLong => 4,
            TypeRational => 4, // two 4-byte halves
            TypeLong8 => 8,
            _ => 1
        };

        if (unit == 1)
            return entry.Data;

        var flipped = (byte[])entry.Data.Clone();
        for (var i = 0; i + unit <= flipped.Length; i += unit)
            Array.Reverse(flipped, i, unit);

        return flipped;
    }

    /// <summary>
    /// A file with a hole in it: a head, a long stretch of nothing, and a tail at a stated offset.
    /// Reports the length the tail implies, so the reader's bounds checks see a real file rather
    /// than a short one, without anything having to allocate four gigabytes.
    /// </summary>
    private sealed class SparseStream(byte[] head, ulong tailOffset, byte[] tail) : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => true;
        public override bool CanWrite => false;
        public override long Length => (long)tailOffset + tail.Length;
        public override long Position { get; set; }

        public override int Read(byte[] buffer, int offset, int count)
        {
            var read = 0;

            while (read < count && Position < Length)
            {
                var source = Position < head.Length ? head
                    : Position >= (long)tailOffset ? tail
                    : null;

                if (source is null)
                {
                    buffer[offset + read] = 0; // the hole
                }
                else
                {
                    var index = Position < head.Length ? Position : Position - (long)tailOffset;
                    if (index >= source.Length)
                    {
                        buffer[offset + read] = 0;
                    }
                    else
                    {
                        buffer[offset + read] = source[index];
                    }
                }

                Position++;
                read++;
            }

            return read;
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            Position = origin switch
            {
                SeekOrigin.Begin => offset,
                SeekOrigin.Current => Position + offset,
                _ => Length + offset
            };

            return Position;
        }

        public override void Flush() { }
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    private static void Write16(Span<byte> destination, bool little, ushort value)
    {
        if (little) 
            BinaryPrimitives.WriteUInt16LittleEndian(destination, value);
        else 
            BinaryPrimitives.WriteUInt16BigEndian(destination, value);
    }

    private static void Write64(Span<byte> destination, bool little, ulong value)
    {
        if (little) 
            BinaryPrimitives.WriteUInt64LittleEndian(destination, value);
        else 
            BinaryPrimitives.WriteUInt64BigEndian(destination, value);
    }
}
