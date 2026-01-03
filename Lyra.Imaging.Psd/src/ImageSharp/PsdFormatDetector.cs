using System.Diagnostics.CodeAnalysis;
using SixLabors.ImageSharp.Formats;

namespace Lyra.Imaging.Psd.ImageSharp;

public sealed class PsdFormatDetector : IImageFormatDetector
{
    public int HeaderSize => 6;
    
    private static ReadOnlySpan<byte> Signature => "8BPS"u8;
    
    public bool TryDetectFormat(ReadOnlySpan<byte> header, [NotNullWhen(true)] out IImageFormat? format)
    {
        format = null;

        if (header.Length < 6)
            return false;

        if (!header[..4].SequenceEqual(Signature))
            return false;

        // 0x0001 = PSD, 0x0002 = PSB
        var version = (ushort)((header[4] << 8) | header[5]);
        if (version != 1 && version != 2)
            return false;

        format = PsdFormat.Instance;
        return true;
    }
}