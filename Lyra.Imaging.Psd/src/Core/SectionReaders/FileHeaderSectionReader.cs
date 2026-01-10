using Lyra.Imaging.Psd.Core.Common;
using Lyra.Imaging.Psd.Core.Primitives;
using Lyra.Imaging.Psd.Core.Readers;
using Lyra.Imaging.Psd.Core.SectionData;

namespace Lyra.Imaging.Psd.Core.SectionReaders;

internal static class FileHeaderSectionReader
{
    public static FileHeader Read(PsdBigEndianReader reader)
    {
        reader.ExpectSignature(PsdSignatures.FileHeader);

        var version = reader.ReadUInt16();
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