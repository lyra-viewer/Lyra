using System.Buffers.Binary;
using System.Text;
using Lyra.Metadata.Support;

namespace Lyra.Metadata.Icc;

/// <summary>
/// What an ICC profile says about itself: the header's color space and the text of its
/// description. Nothing here interprets the color transform - Skia does that when it decodes.
/// </summary>
public sealed class IccProfile : IMetadataBlock<IccProfile>
{
    private const int HeaderLength = 128;
    private const int MaxTags = 1024;

    private static readonly Encoding LenientUtf8 = new UTF8Encoding(false, throwOnInvalidBytes: false);

    /// <summary>The data color space as the header's four characters, padding and all ("RGB ").</summary>
    public string ColorSpace { get; private init; } = string.Empty;

    public string? Description { get; private init; }

    public static IccProfile? Parse(ReadOnlySpan<byte> data)
    {
        if (data.Length < 24)
            return null;

        string? description = null;

        if (data.Length >= HeaderLength + 4)
        {
            var count = BinaryPrimitives.ReadUInt32BigEndian(data[HeaderLength..]);
            for (var i = 0; i < Math.Min(count, MaxTags) && description is null; i++)
            {
                var at = HeaderLength + 4 + i * 12;
                if (at + 12 > data.Length)
                    break;

                var signature = data.Slice(at, 4);
                var offset = BinaryPrimitives.ReadUInt32BigEndian(data[(at + 4)..]);
                var size = BinaryPrimitives.ReadUInt32BigEndian(data[(at + 8)..]);
                if (offset >= data.Length || size > data.Length - offset)
                    continue;

                var tag = data.Slice((int)offset, (int)size);

                if (signature.SequenceEqual("desc"u8))
                    description = ReadText(tag);
            }
        }

        return new IccProfile
        {
            ColorSpace = FourCc(data[16..20]),
            Description = description
        };
    }

    /// <summary>A signature as text; an all-zero one is no signature.</summary>
    private static string FourCc(ReadOnlySpan<byte> bytes) => bytes.ContainsAnyExcept((byte)0) ? LenientUtf8.GetString(bytes) : string.Empty;

    /// <summary>The text of a 'desc', 'text' or 'mluc' tag; null for any other type.</summary>
    private static string? ReadText(ReadOnlySpan<byte> tag)
    {
        if (tag.Length < 8)
            return null;

        var type = tag[..4];
        if (type.SequenceEqual("desc"u8))
            return ReadDesc(tag);

        if (type.SequenceEqual("text"u8))
            return ReadPlainText(tag);

        return type.SequenceEqual("mluc"u8) ? ReadFirstLocalized(tag) : null;
    }

    /// <summary>textDescriptionType: ASCII whose count includes the terminating NUL.</summary>
    private static string? ReadDesc(ReadOnlySpan<byte> tag)
    {
        if (tag.Length < 12)
            return null;

        var count = BinaryPrimitives.ReadUInt32BigEndian(tag[8..]);
        if (count == 0)
            return string.Empty;

        var length = (int)Math.Min(Math.Min(count - 1, (uint)(tag.Length - 12)), Limits.ValueBytes);
        // A count past the terminator takes in its NUL, which is no part of the name.
        return Encoding.ASCII.GetString(ByteText.UntilNul(tag.Slice(12, length)));
    }

    private static string ReadPlainText(ReadOnlySpan<byte> tag)
    {
        var text = ByteText.UntilNul(tag[8..]);
        return Encoding.ASCII.GetString(text[..Math.Min(text.Length, Limits.ValueBytes)]);
    }

    /// <summary>multiLocalizedUnicodeType: the first record whose text lies inside the tag.</summary>
    private static string? ReadFirstLocalized(ReadOnlySpan<byte> tag)
    {
        if (tag.Length < 16)
            return null;

        var count = BinaryPrimitives.ReadUInt32BigEndian(tag[8..]);
        var recordSize = BinaryPrimitives.ReadUInt32BigEndian(tag[12..]);
        if (recordSize < 12)
            return null;

        for (var i = 0; i < Math.Min(count, MaxTags); i++)
        {
            var at = 16 + i * (long)recordSize;
            if (at + 12 > tag.Length)
                break;

            var record = tag.Slice((int)at, 12);
            var length = BinaryPrimitives.ReadUInt32BigEndian(record[4..]);
            var offset = BinaryPrimitives.ReadUInt32BigEndian(record[8..]);
            if (offset > tag.Length || length > tag.Length - offset || length > Limits.ValueBytes)
                continue;

            // Some writers count the UTF-16 NUL that ends the string; it is no part of the name.
            return ByteText.UntilNul(Encoding.BigEndianUnicode.GetString(tag.Slice((int)offset, (int)length)));
        }

        return null;
    }
}