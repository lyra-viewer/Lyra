using System.Buffers.Binary;
using Lyra.Imaging.Decoding.Support;
using Lyra.Imaging.Metadata;

namespace Lyra.Imaging.Decoding.Decoders.J2K;

/// <summary>
/// Reads the declared size of a JPEG 2000 image from its header, so an impossible one is refused
/// before OpenJPEG allocates for it.
/// </summary>
internal static class J2KHeader
{
    private const ushort StartOfCodestream = 0xFF4F;
    private const ushort ImageAndTileSize = 0xFF51;

    public readonly record struct Size(long Width, long Height, int Components);

    /// <summary>
    /// Refuses an impossible size from the header alone; OpenJPEG would allocate and decode all of
    /// it before the decoded dimensions could be checked.
    /// </summary>
    public static void RequireDeclaredSizeFits(ReadOnlySpan<byte> data)
    {
        if (!TryRead(data, out var declared))
            return;

        DecoderValidation.RequireSaneDimensions(ClampToInt(declared.Width), ClampToInt(declared.Height));

        // OpenJPEG holds every component as a 32-bit int, beside the RGBA it is converted to.
        DecoderValidation.RequireAvailableMemory(declared.Width, declared.Height, 4 + 4 * Math.Max(1, declared.Components));
    }

    private static int ClampToInt(long value) => (int)Math.Min(value, int.MaxValue);

    public static bool TryRead(ReadOnlySpan<byte> data, out Size size) =>
        data.StartsWith(IsoBoxMetadata.Jp2Signature)
            ? TryReadJp2(data, out size)
            : TryReadCodestream(data, out size);

    /// <summary>The image header box inside the JP2 header box, or else the codestream box.</summary>
    private static bool TryReadJp2(ReadOnlySpan<byte> data, out Size size)
    {
        var offset = 0;
        while (IsoBoxMetadata.TryReadBox(data, ref offset, out var type, out var payload))
        {
            if (type == "jp2h" && TryReadImageHeader(payload, out size))
                return true;

            if (type == "jp2c")
                return TryReadCodestream(payload, out size);
        }

        size = default;
        return false;
    }

    private static bool TryReadImageHeader(ReadOnlySpan<byte> header, out Size size)
    {
        var offset = 0;
        while (IsoBoxMetadata.TryReadBox(header, ref offset, out var type, out var payload))
        {
            if (type != "ihdr" || payload.Length < 10)
                continue;

            size = new Size(
                Width: BinaryPrimitives.ReadUInt32BigEndian(payload[4..]),
                Height: BinaryPrimitives.ReadUInt32BigEndian(payload),
                Components: BinaryPrimitives.ReadUInt16BigEndian(payload[8..])
            );

            return true;
        }

        size = default;
        return false;
    }

    /// <summary>The SIZ marker, which a codestream must carry straight after its start marker.</summary>
    private static bool TryReadCodestream(ReadOnlySpan<byte> codestream, out Size size)
    {
        size = default;

        if (codestream.Length < 42
            || BinaryPrimitives.ReadUInt16BigEndian(codestream) != StartOfCodestream
            || BinaryPrimitives.ReadUInt16BigEndian(codestream[2..]) != ImageAndTileSize)
        {
            return false;
        }

        var siz = codestream[6..];

        long xEnd = BinaryPrimitives.ReadUInt32BigEndian(siz[2..]);
        long yEnd = BinaryPrimitives.ReadUInt32BigEndian(siz[6..]);
        long xOrigin = BinaryPrimitives.ReadUInt32BigEndian(siz[10..]);
        long yOrigin = BinaryPrimitives.ReadUInt32BigEndian(siz[14..]);
        int components = BinaryPrimitives.ReadUInt16BigEndian(siz[34..]);

        if (xEnd <= xOrigin || yEnd <= yOrigin)
            return false;

        size = new Size(xEnd - xOrigin, yEnd - yOrigin, components);
        return true;
    }
}