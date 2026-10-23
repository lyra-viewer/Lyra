using System.Text;
using Lyra.Metadata.Exif;
using Lyra.Metadata.Layout;
using Lyra.Metadata.Support;

namespace Lyra.Metadata.Containers;

public sealed record PngFacts(int BitDepth, int ColorType, int CompressionMethod, string? IccName) : IFormatFacts;

/// <summary>
/// Finds the metadata in a PNG's chunks, in file order: EXIF in eXIf, XMP in iTXt or zTXt under the
/// "XML:com.adobe.xmp" keyword, the ICC profile in iCCP, and IHDR's facts. A chunk the format
/// allows once is taken the first time; CRCs are not checked; a file cut short keeps the chunks
/// that are whole.
/// </summary>
public sealed class PngContainer : IBufferedContainer
{
    private static ReadOnlySpan<byte> Signature => PngChunks.Signature;

    private const string XmpKeyword = "XML:com.adobe.xmp";
    private const int MaxChunks = 100_000;

    public static bool Recognizes(ReadOnlySpan<byte> head) => head.StartsWith(Signature) && head.Length >= 16 && head[12..16].SequenceEqual("IHDR"u8);

    public static ContainerContents? Read(ReadOnlySpan<byte> data)
    {
        if (!Recognizes(data))
            return null;

        var result = new ContainerContents { DamageWithNothingFails = true };
        var seen = new HashSet<string>();
        var inflater = new Inflater();
        PngFacts? header = null;
        string? iccName = null;

        var end = Signature.Length;
        var stopped = false;
        var chunks = 0;

        foreach (var chunk in PngChunks.Children(data))
        {
            if (!IsChunkType(chunk.Type))
            {
                result.Damaged = stopped = true;
                break;
            }

            end = chunk.Offset + chunk.TotalBytes;
            if (chunk.Is("IEND"u8) || ++chunks > MaxChunks)
            {
                stopped = true;
                break;
            }

            var type = Encoding.ASCII.GetString(chunk.Type);
            var body = chunk.Body;
            switch (type)
            {
                case "IHDR" when seen.Add(type) && body.Length >= 13:
                    header = new PngFacts(body[8], body[9], body[10], IccName: null);
                    break;

                case "eXIf" when seen.Add(type):
                    result.Add(MetadataBlockKind.Exif, body.ToArray());
                    break;

                case "iCCP" when seen.Add(type):
                    ReadIccp(body, inflater, result, out iccName);
                    break;

                case "iTXt" when ReadITxt(body, inflater) is { } text:
                    AddText(text, result);
                    break;

                case "zTXt" when ReadZTxt(body, inflater) is { } text:
                    AddText(text, result);
                    break;
            }
        }

        if (!stopped)
            result.Damaged = end < data.Length;

        if (header is not null)
            result.AddFacts(header with { IccName = iccName });

        return result;
    }

    /// <summary>A compressed or international text chunk's keyword and text.</summary>
    private sealed record TextChunk(string Keyword, byte[] Text);

    private const string RawProfilePrefix = "Raw profile type ";

    /// <summary>XMP under its own keyword, and the profiles ImageMagick stores as hex text.</summary>
    private static void AddText(TextChunk chunk, ContainerContents result)
    {
        if (chunk.Keyword == XmpKeyword)
        {
            result.Add(MetadataBlockKind.Xmp, chunk.Text);
            return;
        }

        if (!chunk.Keyword.StartsWith(RawProfilePrefix, StringComparison.Ordinal))
            return;

        MetadataBlockKind? kind = chunk.Keyword[RawProfilePrefix.Length..] switch
        {
            "exif" or "APP1" => MetadataBlockKind.Exif,
            "xmp" => MetadataBlockKind.Xmp,
            "iptc" => MetadataBlockKind.Iptc,
            "icc" => MetadataBlockKind.Icc,
            _ => null
        };

        if (kind is not { } blockKind || RawProfile(chunk.Text) is not { } data)
            return;

        if (blockKind == MetadataBlockKind.Exif)
            data = ExifPayload.WithoutPreamble(data).ToArray();

        result.Add(blockKind, data);
    }

    /// <summary>
    /// ImageMagick's form: a newline, the profile's name, its length in bytes, then the bytes as
    /// hex, wrapped across lines. Only as many bytes as the length says are taken.
    /// </summary>
    private static byte[]? RawProfile(byte[] text)
    {
        var lines = Encoding.ASCII.GetString(text).Split('\n', 4);
        if (lines.Length < 4 || !int.TryParse(lines[2].Trim(), out var length) || length is < 0 or > Limits.AssembledBytes)
            return null;

        var bytes = new List<byte>(length);
        var high = -1;
        foreach (var c in lines[3])
        {
            if (bytes.Count == length)
                break;

            var nibble = c switch
            {
                >= '0' and <= '9' => c - '0',
                >= 'a' and <= 'f' => c - 'a' + 10,
                >= 'A' and <= 'F' => c - 'A' + 10,
                _ => -1
            };

            if (nibble < 0)
                continue;

            if (high < 0)
            {
                high = nibble;
                continue;
            }

            bytes.Add((byte)(high << 4 | nibble));
            high = -1;
        }

        return bytes.ToArray();
    }

    /// <summary>A profile name, a NUL, the compression method (0, zlib), then the compressed profile.</summary>
    private static void ReadIccp(ReadOnlySpan<byte> body, Inflater inflater, ContainerContents result, out string? name)
    {
        name = null;
        var end = body.IndexOf((byte)0);
        if (end < 0)
            return;

        name = Encoding.Latin1.GetString(body[..end]);
        if (end + 1 < body.Length && body[end + 1] == 0 && inflater.Inflate(body[(end + 2)..]) is { } profile)
            result.Add(MetadataBlockKind.Icc, profile);
    }

    /// <summary>Keyword, NUL, compression flag and method, language tag, NUL, translated keyword, NUL, text.</summary>
    private static TextChunk? ReadITxt(ReadOnlySpan<byte> body, Inflater inflater)
    {
        var keywordEnd = body.IndexOf((byte)0);
        if (keywordEnd < 0 || keywordEnd + 3 > body.Length)
            return null;

        var keyword = Encoding.Latin1.GetString(body[..keywordEnd]);

        var compressed = body[keywordEnd + 1] == 1;
        var rest = body[(keywordEnd + 3)..];

        var languageEnd = rest.IndexOf((byte)0);
        if (languageEnd < 0)
            return null;

        rest = rest[(languageEnd + 1)..];
        var translatedEnd = rest.IndexOf((byte)0);
        if (translatedEnd < 0)
            return null;

        var text = rest[(translatedEnd + 1)..];
        return (compressed ? inflater.Inflate(text) : text.ToArray()) is { } bytes ? new TextChunk(keyword, bytes) : null;
    }

    /// <summary>Keyword, NUL, compression method, then the compressed text.</summary>
    private static TextChunk? ReadZTxt(ReadOnlySpan<byte> body, Inflater inflater)
    {
        var keywordEnd = body.IndexOf((byte)0);
        if (keywordEnd < 0 || keywordEnd + 2 > body.Length)
            return null;

        return inflater.Inflate(body[(keywordEnd + 2)..]) is { } text ? new TextChunk(Encoding.Latin1.GetString(body[..keywordEnd]), text) : null;
    }

    /// <summary>zlib inflation with one budget for the whole file, so no number of chunks can make it unbounded.</summary>
    private sealed class Inflater
    {
        private long _remaining = Limits.AssembledBytes;

        public byte[]? Inflate(ReadOnlySpan<byte> compressed)
        {
            var inflated = Decompression.Zlib(compressed, _remaining);
            _remaining -= inflated?.Length ?? 0;
            return inflated;
        }
    }

    private static bool IsChunkType(ReadOnlySpan<byte> type)
    {
        foreach (var b in type)
            if (b is not ((>= (byte)'A' and <= (byte)'Z') or (>= (byte)'a' and <= (byte)'z')))
                return false;

        return true;
    }
}