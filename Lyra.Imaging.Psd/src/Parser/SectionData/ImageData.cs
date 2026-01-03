using Lyra.Imaging.Psd.Parser.Common;

namespace Lyra.Imaging.Psd.Parser.SectionData;

public readonly record struct ImageData(
    long PayloadOffset, // Points to the first byte AFTER the 2-byte compression field.
    long PayloadLength, // Until EOF (TODO or computed)
    CompressionType CompressionType
);