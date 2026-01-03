using System.Text;

namespace Lyra.Imaging.Psd.Parser.Primitives;

internal static class FourCC
{
    public static uint FromString(string s)
    {
        ArgumentNullException.ThrowIfNull(s);
        
        if (s.Length != 4) 
            throw new ArgumentException("FourCC must be exactly 4 ASCII characters.", nameof(s));

        // PSD uses big-endian FourCC packing.
        return ((uint)s[0] << 24) | ((uint)s[1] << 16) | ((uint)s[2] << 8) | s[3];
    }
    
    public static string ToString(uint v)
    {
        Span<byte> b = stackalloc byte[4];
        b[0] = (byte)((v >> 24) & 0xFF);
        b[1] = (byte)((v >> 16) & 0xFF);
        b[2] = (byte)((v >> 8) & 0xFF);
        b[3] = (byte)(v & 0xFF);
        return Encoding.ASCII.GetString(b);
    }
}