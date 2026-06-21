using System.Buffers.Binary;
using System.Text;

namespace Lyra.ManagedCodecs.Texture.Ktx;

/// <summary>
/// Bits common to both Khronos Texture readers: magic-byte detection, the key/value metadata parse
/// (used to recover image orientation), and the dimension/mip sanity bounds. Both readers treat input
/// as hostile, so these helpers validate rather than trust.
/// </summary>
public static class KtxShared
{
    public static ReadOnlySpan<byte> Ktx1Identifier => [0xAB, 0x4B, 0x54, 0x58, 0x20, 0x31, 0x31, 0xBB, 0x0D, 0x0A, 0x1A, 0x0A];

    public static ReadOnlySpan<byte> Ktx2Identifier => [0xAB, 0x4B, 0x54, 0x58, 0x20, 0x32, 0x30, 0xBB, 0x0D, 0x0A, 0x1A, 0x0A];

    public static bool IsKtx1(ReadOnlySpan<byte> header)
        => header.Length >= 12 && header[..12].SequenceEqual(Ktx1Identifier);

    public static bool IsKtx2(ReadOnlySpan<byte> header)
        => header.Length >= 12 && header[..12].SequenceEqual(Ktx2Identifier);

    /// <summary>
    /// Reads the <c>KTXorientation</c> metadata to decide whether the stored rows run top-down or
    /// bottom-up. KTX1 writes <c>"S=r,T=d"</c>-style values and defaults to the OpenGL bottom-left
    /// convention when absent; KTX2 writes a compact <c>"rd"</c> and defaults to top-left. The "T"
    /// (vertical) axis is the only one that matters for a 2D display flip: <c>d</c> = down = top-left.
    /// </summary>
    public static TextureOrigin ReadOrigin(ReadOnlySpan<byte> keyValueData, bool isKtx2, bool bigEndian = false)
    {
        var orientation = FindValue(keyValueData, "KTXorientation", bigEndian);
        if (orientation is null)
        {
            return isKtx2 ? TextureOrigin.TopLeft : TextureOrigin.BottomLeft;
        }

        if (isKtx2)
        {
            // Compact form: axis directions by position, vertical at index 1 ("rd", "ru", ...).
            var down = orientation.Length >= 2 && (orientation[1] == 'd' || orientation[1] == 'D');
            return down ? TextureOrigin.TopLeft : TextureOrigin.BottomLeft;
        }

        // Verbose form: a comma-separated list of "axis=dir" pairs; "T=d" is top-left.
        return orientation.Contains("T=d", StringComparison.OrdinalIgnoreCase) ? TextureOrigin.TopLeft : TextureOrigin.BottomLeft;
    }

    /// <summary>
    /// Finds one key's value in a KTX key/value block. Entries are
    /// <c>[uint32 keyAndValueByteSize][key\0value][padding to 4]</c>; the key is a NUL-terminated
    /// string and the value is the remaining bytes, returned here as text. Malformed or truncated
    /// entries terminate the scan rather than throw - the metadata is advisory and a bad block
    /// must not sink an otherwise valid texture.
    /// </summary>
    private static string? FindValue(ReadOnlySpan<byte> kv, string key, bool bigEndian)
    {
        var offset = 0;
        while (offset + 4 <= kv.Length)
        {
            var sizeField = kv.Slice(offset, 4);
            var size = (int)(bigEndian
                ? BinaryPrimitives.ReadUInt32BigEndian(sizeField)
                : BinaryPrimitives.ReadUInt32LittleEndian(sizeField));
            offset += 4;
            if (size <= 0 || offset + size > kv.Length)
            {
                break;
            }

            var entry = kv.Slice(offset, size);
            var nul = entry.IndexOf((byte)0);
            if (nul > 0)
            {
                var entryKey = Encoding.UTF8.GetString(entry[..nul]);
                if (entryKey == key)
                {
                    return Encoding.UTF8.GetString(entry[(nul + 1)..]).TrimEnd('\0');
                }
            }

            offset += size;
            offset = (offset + 3) & ~3; // value padding to the next 4-byte boundary
        }

        return null;
    }
}