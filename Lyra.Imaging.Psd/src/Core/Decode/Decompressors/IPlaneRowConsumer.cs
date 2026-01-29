namespace Lyra.Imaging.Psd.Core.Decode.Decompressors;

/// <summary>
/// Receives decoded planar rows (one plane at a time).
/// 
/// Delivery order is decompressor-dependent:
/// - Some implementations may deliver plane-major (all rows of a plane).
/// - Others may deliver row-major (all planes for a row).
/// 
/// Consumers MUST NOT assume any specific ordering unless explicitly documented
/// by the calling decompressor.
/// </summary>
public interface IPlaneRowConsumer
{
    void ConsumeRow(int planeIndex, int y, ReadOnlySpan<byte> row);
}