using System.Buffers.Binary;
using System.Text;
using Lyra.Common;
using MetadataExtractor.Formats.Exif;
using MetadataExtractor.Formats.Icc;
using MetadataExtractor.Formats.Iptc;
using MetadataExtractor.Formats.Xmp;
using MetadataExtractor.IO;
using MetadataExtractor;
using Directory = MetadataExtractor.Directory;

namespace Lyra.Imaging.Metadata;

/// <summary>
/// Reads the metadata directories out of a BigTIFF.
///
/// MetadataExtractor cannot: its TIFF reader is built on 32-bit offsets, and a BigTIFF - which
/// exists precisely because a file outgrew them - makes it throw an <see cref="OverflowException"/>
/// before a single tag is read.
///
/// What comes back is the same set of <see cref="Directory"/> objects MetadataExtractor would have
/// produced for an ordinary TIFF, so everything downstream - <see cref="ExifReader"/>,
/// <see cref="FormatHeaderReader"/>, <see cref="DescriptiveReader"/> - works on a BigTIFF without
/// knowing one exists.
///
/// BigTIFF differs from TIFF in three places and nowhere else: the version is 43 rather than 42,
/// offsets are 8 bytes rather than 4, and an IFD entry is 20 bytes with a 64-bit count rather than
/// 12 with a 32-bit one. The tags themselves are identical.
/// </summary>
internal static class BigTiffMetadataReader
{
    private const ushort BigTiffVersion = 43;

    /// <summary>Header is 16 bytes: order, version, offset size, reserved, then the first IFD.</summary>
    private const int HeaderLength = 16;

    private const int EntryLength = 20;
    
    private const ulong MaxEntriesPerIfd = 4096;

    private const ulong MaxValuesPerTag = 4096;

    private const ulong MaxBlockLength = 8 * 1024 * 1024;

    // Tags that point at other directories rather than carrying a value.
    private const int TagExifIfdPointer = 0x8769;
    private const int TagGpsIfdPointer = 0x8825;
    private const int TagXmp = 0x02BC;
    private const int TagIptc = 0x83BB;
    private const int TagIccProfile = 0x8773;

    /// <summary>
    /// Tags whose values are one entry per strip or per palette slot. They are worth nothing to
    /// the panel and are the only ones large enough to be worth refusing on size alone.
    /// </summary>
    private static readonly HashSet<int> BulkTags = [273, 279, 288, 289, 320, 324, 325, 330];
    
    public static bool IsBigTiff(Stream stream)
    {
        if (!stream.CanSeek)
            return false;

        var position = stream.Position;
        try
        {
            stream.Position = 0;

            Span<byte> header = stackalloc byte[4];
            if (stream.ReadAtLeast(header, header.Length, throwOnEndOfStream: false) < header.Length)
                return false;

            if (!TryReadByteOrder(header, out var little))
                return false;

            return Read16(header[2..], little) == BigTiffVersion;
        }
        catch (Exception ex) when (ex is IOException or NotSupportedException or ObjectDisposedException)
        {
            return false;
        }
        finally
        {
            if (stream.CanSeek)
                stream.Position = position;
        }
    }

    /// <inheritdoc cref="IsBigTiff(Stream)"/>
    public static bool IsBigTiff(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            return IsBigTiff(stream);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>
    /// Reads what the first directory holds, plus the subdirectories it points at. Only the first
    /// is read: in a BigTIFF the later ones are the pyramid levels or the pages, and their tags
    /// describe those rather than the image.
    /// </summary>
    public static IReadOnlyList<Directory> Read(Stream stream)
    {
        var directories = new List<Directory>();

        try
        {
            if (!stream.CanSeek)
                return directories;

            Span<byte> header = stackalloc byte[HeaderLength];
            stream.Position = 0;
            if (stream.ReadAtLeast(header, HeaderLength, throwOnEndOfStream: false) < HeaderLength)
                return directories;

            if (!TryReadByteOrder(header, out var little) || Read16(header[2..], little) != BigTiffVersion)
                return directories;
            
            if (Read16(header[4..], little) != 8 || Read16(header[6..], little) != 0)
                return directories;

            var cursor = new Cursor(stream, little);
            var ifd0 = new ExifIfd0Directory();

            var pointers = ReadIfd(cursor, Read64(header[8..], little), ifd0, directories);

            if (ifd0.TagCount > 0)
                directories.Add(ifd0);

            if (pointers.Exif != 0)
                ReadPointedIfd(cursor, pointers.Exif, new ExifSubIfdDirectory(), directories);

            if (pointers.Gps != 0)
                ReadPointedIfd(cursor, pointers.Gps, new GpsDirectory(), directories);
        }
        catch (Exception ex)
        {
            // Whatever was gathered before the file stopped making sense is still worth showing.
            Logger.Warning($"[BigTiffReader] Stopped reading after {directories.Count} directories: {ex.Message}");
        }

        return directories;
    }

    /// <inheritdoc cref="Read(Stream)"/>
    public static IReadOnlyList<Directory> Read(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            return Read(stream);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Logger.Warning($"[BigTiffReader] Could not open '{path}': {ex.Message}");
            return [];
        }
    }

    private static void ReadPointedIfd(Cursor cursor, ulong offset, Directory directory, List<Directory> directories)
    {
        ReadIfd(cursor, offset, directory, directories);

        if (directory.TagCount > 0)
            directories.Add(directory);
    }

    /// <summary>
    /// Walks one directory into <paramref name="directory"/>, handing the blocks that are whole
    /// formats of their own - XMP, IPTC, ICC - to the readers that already know them.
    /// </summary>
    private static (ulong Exif, ulong Gps) ReadIfd(Cursor cursor, ulong offset, Directory directory, List<Directory> directories)
    {
        var pointers = (Exif: 0UL, Gps: 0UL);

        Span<byte> countBuffer = stackalloc byte[8];
        if (offset < HeaderLength || !cursor.TryRead(offset, countBuffer))
            return pointers;

        var entries = Read64(countBuffer, cursor.Little);
        if (entries is 0 or > MaxEntriesPerIfd)
            return pointers;

        var entry = new byte[EntryLength];

        for (ulong i = 0; i < entries; i++)
        {
            if (!cursor.TryRead(offset + 8 + i * EntryLength, entry))
                break;

            var tag = Read16(entry, cursor.Little);
            var type = Read16(entry.AsSpan(2), cursor.Little);
            var count = Read64(entry.AsSpan(4), cursor.Little);
            var valueField = entry.AsSpan(12, 8);

            switch (tag)
            {
                case TagExifIfdPointer:
                    pointers.Exif = ReadPointer(cursor, type, count, valueField);
                    continue;

                case TagGpsIfdPointer:
                    pointers.Gps = ReadPointer(cursor, type, count, valueField);
                    continue;
            }

            if (ReadData(cursor, type, count, valueField, tag) is not { } data)
                continue;

            switch (tag)
            {
                case TagXmp:
                    AddXmp(data, directories);
                    continue;
                case TagIptc:
                    AddIptc(data, directories);
                    continue;
                case TagIccProfile:
                    AddIcc(data, directories);
                    continue;
            }

            if (Decode(type, count, data, cursor.Little) is { } value)
                directory.Set(tag, value);
        }

        return pointers;
    }
    
    private static ulong ReadPointer(Cursor cursor, ushort type, ulong count, ReadOnlySpan<byte> valueField)
    {
        if (count != 1)
            return 0;

        return type switch
        {
            4 or 13 => Read32(valueField, cursor.Little),
            16 or 18 => Read64(valueField, cursor.Little),
            _ => 0
        };
    }
    
    private static byte[]? ReadData(Cursor cursor, ushort type, ulong count, ReadOnlySpan<byte> valueField, int tag)
    {
        var unit = (ulong)SizeOf(type);
        if (unit == 0 || count == 0 || count > MaxValuesPerTag || BulkTags.Contains(tag))
            return null;

        if (count > MaxBlockLength / unit)
            return null;

        var length = count * unit;
        if (length > MaxBlockLength)
            return null;

        if (length <= 8)
            return valueField[..(int)length].ToArray();

        var data = new byte[length];
        return cursor.TryRead(Read64(valueField, cursor.Little), data) ? data : null;
    }

    private static object? Decode(ushort type, ulong count, byte[] data, bool little)
    {
        var n = (int)count;
        
        return type switch
        {
            // ASCII, NUL-terminated in the file and not in the panel.
            2               => Encoding.UTF8.GetString(data).TrimEnd('\0', ' '),
            
            // UNDEFINED is a byte block with no interpretation of its own.
            7               => data,
            
            1               => Scalar(n, i => (int)data[i]),
            6               => Scalar(n, i => (int)(sbyte)data[i]),
            3               => Scalar(n, i => (int)Read16(data.AsSpan(i * 2), little)),
            8               => Scalar(n, i => (int)(short)Read16(data.AsSpan(i * 2), little)),
            4 or 13         => Scalar(n, i => (long)Read32(data.AsSpan(i * 4), little)),
            9               => Scalar(n, i => (long)(int)Read32(data.AsSpan(i * 4), little)),
            16 or 17 or 18  => Scalar(n, i => (long)Read64(data.AsSpan(i * 8), little)),
            5               => Scalar(n, i => new Rational(Read32(data.AsSpan(i * 8), little), Read32(data.AsSpan(i * 8 + 4), little))),
            10              => Scalar(n, i => new Rational((int)Read32(data.AsSpan(i * 8), little), (int)Read32(data.AsSpan(i * 8 + 4), little))),
            11              => Scalar(n, i => BitConverter.Int32BitsToSingle((int)Read32(data.AsSpan(i * 4), little))),
            12              => Scalar(n, i => BitConverter.Int64BitsToDouble((long)Read64(data.AsSpan(i * 8), little))),
            _               => null
        };
    }
    
    private static object Scalar<T>(int count, Func<int, T> at)
    {
        if (count == 1)
            return at(0)!;

        var values = new T[count];
        for (var i = 0; i < count; i++)
            values[i] = at(i);

        return values;
    }

    private static void AddXmp(byte[] data, List<Directory> directories)
    {
        try
        {
            directories.Add(new XmpReader().Extract(data));
        }
        catch (Exception ex)
        {
            Logger.Debug($"[BigTiffReader] XMP block could not be read: {ex.Message}");
        }
    }

    private static void AddIptc(byte[] data, List<Directory> directories)
    {
        try
        {
            directories.Add(new IptcReader().Extract(new SequentialByteArrayReader(data), data.Length));
        }
        catch (Exception ex)
        {
            Logger.Debug($"[BigTiffReader] IPTC block could not be read: {ex.Message}");
        }
    }

    private static void AddIcc(byte[] data, List<Directory> directories)
    {
        try
        {
            directories.Add(new IccReader().Extract(new ByteArrayReader(data)));
        }
        catch (Exception ex)
        {
            Logger.Debug($"[BigTiffReader] ICC profile could not be read: {ex.Message}");
        }
    }

    private static int SizeOf(ushort type) => type switch
    {
        1 or 2 or 6 or 7                => 1,
        3 or 8                          => 2,
        4 or 9 or 11 or 13              => 4,
        5 or 10 or 12 or 16 or 17 or 18 => 8,
        _                               => 0
    };

    private static bool TryReadByteOrder(ReadOnlySpan<byte> header, out bool little)
    {
        little = header[0] == 'I' && header[1] == 'I';
        return little || (header[0] == 'M' && header[1] == 'M');
    }

    private static ushort Read16(ReadOnlySpan<byte> source, bool little) => little
        ? BinaryPrimitives.ReadUInt16LittleEndian(source)
        : BinaryPrimitives.ReadUInt16BigEndian(source);

    private static uint Read32(ReadOnlySpan<byte> source, bool little) => little
        ? BinaryPrimitives.ReadUInt32LittleEndian(source)
        : BinaryPrimitives.ReadUInt32BigEndian(source);

    private static ulong Read64(ReadOnlySpan<byte> source, bool little) => little
        ? BinaryPrimitives.ReadUInt64LittleEndian(source)
        : BinaryPrimitives.ReadUInt64BigEndian(source);
    
    private sealed class Cursor(Stream stream, bool little)
    {
        private readonly long _length = stream.Length;

        public bool Little { get; } = little;

        public bool TryRead(ulong offset, Span<byte> buffer)
        {
            if (offset > (ulong)_length || (ulong)_length - offset < (ulong)buffer.Length)
                return false;

            stream.Position = (long)offset;
            return stream.ReadAtLeast(buffer, buffer.Length, throwOnEndOfStream: false) == buffer.Length;
        }
    }
}