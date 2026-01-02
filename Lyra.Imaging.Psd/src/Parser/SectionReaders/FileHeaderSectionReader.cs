using Lyra.Imaging.Psd.Parser.PsdReader;
using Lyra.Imaging.Psd.Parser.SectionData;

namespace Lyra.Imaging.Psd.Parser.SectionReaders;

internal static class FileHeaderSectionReader
{
    private static ReadOnlySpan<byte> Signature => "8BPS"u8;
    
    public static FileHeader Read(PsdBigEndianReader reader)
    {
        reader.ExpectSignature(Signature);

        ushort version = reader.ReadUInt16();
        if (version is not (1 or 2))
            throw new NotSupportedException($"Not supported file version: {version}");

        reader.Skip(6);

        var channels = reader.ReadUInt16();
        var height = reader.ReadInt32();
        var width  = reader.ReadInt32();
        var depth = reader.ReadUInt16();
        var colorMode = (ColorMode)reader.ReadUInt16();
        
        if (width <= 0 || height <= 0)
            throw new InvalidDataException($"Invalid PSD dimensions: {width}x{height}.");

        if (channels is 0 or > 56)
            throw new InvalidDataException($"Invalid channel count: {channels}.");

        return new FileHeader(version, channels, width, height, depth, colorMode);
    }
}