using System.Text;
using Lyra.Imaging.Psd.Core.Primitives;
using Lyra.Imaging.Psd.Core.Readers;

namespace Lyra.Imaging.Psd.Core.Decode.Color.ColorCalibration;

/// <summary>
/// Minimal, dependency-free ICC profile name extractor.
/// <list type="bullet">
///   <item><description>Attempts to read the Profile Description from the <c>'desc'</c> tag.</description></item>
///   <item><description>Supports ICC v2 (<c>'desc'</c> tag type = <c>'desc'</c>).</description></item>
///   <item><description>Supports ICC v4 (<c>'desc'</c> tag type often = <c>'mluc'</c>).</description></item>
/// </list>
/// </summary>
public static class IccProfileNameExtractor
{
    public static bool TryGetProfileName(byte[] iccBytes, out string name)
    {
        name = string.Empty;
        if (iccBytes.Length < 132)
            return false;

        using var ms = new MemoryStream(iccBytes, writable: false);
        var reader = new PsdBigEndianReader(ms);

        try
        {
            var fileLen = (long)iccBytes.Length;

            // ICC header: tag table count at offset 128
            reader.Position = 128;
            var tagCount = reader.ReadUInt32();

            // Tag table must fit: 4 bytes for count + tagCount * 12 bytes per entry
            var tagTableBytes = 4L + (long)tagCount * 12L;
            if (128L + tagTableBytes > fileLen)
                return false;

            for (var i = 0; i < tagCount; i++)
            {
                var tagSig = reader.ReadUInt32();
                var offset = reader.ReadUInt32();
                var length = reader.ReadUInt32();

                if (tagSig != PsdSignatures.IccTagDesc || length < 12)
                    continue;

                // Validate tag bounds
                var tagStart = (long)offset;
                var tagEnd = tagStart + length;
                if (tagEnd > fileLen)
                    return false;

                reader.Position = offset;
                var tagType = reader.ReadUInt32();
                reader.Skip(4); // reserved

                if (tagType == PsdSignatures.IccTypeDesc)
                {
                    // ICC v2 ASCII description
                    if (reader.Position + 4 > tagEnd)
                        return false;

                    var asciiLen = reader.ReadUInt32();
                    // asciiLen includes null terminator in typical profiles
                    if (asciiLen <= 1 || asciiLen > length)
                        return false;

                    var remainingInTag = tagEnd - reader.Position;
                    if (asciiLen - 1 > remainingInTag)
                        return false;

                    var buf = new byte[asciiLen - 1]; // exclude null terminator
                    reader.ReadExactly(buf);

                    var raw = Encoding.ASCII.GetString(buf);
                    name = SanitizeProfileName(raw);
                    return name.Length > 0;
                }

                if (tagType == PsdSignatures.IccTypeMluc)
                {
                    // ICC v4 localized Unicode
                    if (reader.Position + 8 > tagEnd)
                        return false;

                    var recordCount = reader.ReadUInt32();
                    var recordSize = reader.ReadUInt32();

                    // Each mluc record is at least 12 bytes (lang(2)+country(2)+len(4)+off(4))
                    if (recordSize < 12)
                        return false;

                    var recordsBase = reader.Position;

                    // Ensure records table fits in the tag
                    var recordsBytes = (long)recordCount * recordSize;
                    if (recordsBase + recordsBytes > tagEnd)
                        return false;

                    string? fallback = null;

                    for (var r = 0; r < recordCount; r++)
                    {
                        reader.Position = recordsBase + r * recordSize;

                        var lang = reader.ReadUInt16();
                        var country = reader.ReadUInt16();
                        var strLen = reader.ReadUInt32();
                        var strOffset = reader.ReadUInt32();

                        var textPos = (long)offset + strOffset;
                        var textEnd = textPos + strLen;

                        // Validate localized string bounds inside file + inside tag
                        if (textPos < tagStart || textEnd > tagEnd || textEnd > fileLen)
                            continue;

                        reader.Position = (uint)textPos;

                        var buf = new byte[strLen];
                        reader.ReadExactly(buf);

                        var raw = Encoding.BigEndianUnicode.GetString(buf);
                        var text = SanitizeProfileName(raw);
                        if (text.Length == 0)
                            continue;

                        // Prefer en-US
                        if (lang == 0x656E && country == 0x5553)
                        {
                            name = text;
                            return true;
                        }

                        fallback ??= text;
                    }

                    if (fallback != null)
                    {
                        name = fallback;
                        return true;
                    }

                    return false;
                }

                // Found 'desc' but unsupported type
                return false;
            }
        }
        catch
        {
            return false;
        }

        return false;
    }

    /// <summary>
    /// Convenience overload.
    /// </summary>
    public static bool TryGetProfileName(ReadOnlySpan<byte> iccBytes, out string name)
        => TryGetProfileName(iccBytes.ToArray(), out name);

    public static string? GetProfileNameOrNull(byte[]? iccBytes)
        => iccBytes != null && TryGetProfileName(iccBytes, out var n) ? n : null;

    private static string SanitizeProfileName(string s)
    {
        if (string.IsNullOrEmpty(s))
            return string.Empty;

        // Remove common padding first
        s = s.TrimEnd('\0', '\u0000', '\uFFFD');

        // Strip control characters (keeps normal Unicode letters/symbols)
        var sb = new StringBuilder(s.Length);
        foreach (var ch in s)
        {
            if (!char.IsControl(ch))
                sb.Append(ch);
        }

        return sb.ToString().Trim();
    }
}