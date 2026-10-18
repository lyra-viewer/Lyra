using System.Text;

namespace Lyra.Metadata.Support;

internal static class ByteText
{
    private static readonly Encoding StrictUtf8 = new UTF8Encoding(false, throwOnInvalidBytes: true);

    public static ReadOnlySpan<byte> UntilNul(ReadOnlySpan<byte> bytes)
    {
        var end = bytes.IndexOf((byte)0);
        return end < 0 ? bytes : bytes[..end];
    }

    public static string UntilNul(string text)
    {
        var end = text.IndexOf('\0');
        return end < 0 ? text : text[..end];
    }

    public static string Utf8OrLatin1(ReadOnlySpan<byte> bytes)
    {
        try
        {
            return StrictUtf8.GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            return Encoding.Latin1.GetString(bytes);
        }
    }
}