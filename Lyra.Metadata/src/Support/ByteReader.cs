using System.Buffers.Binary;

namespace Lyra.Metadata.Support;

internal ref struct ByteReader(ReadOnlySpan<byte> data, bool littleEndian)
{
    private readonly ReadOnlySpan<byte> _data = data;

    public int Position { get; private set; }

    public bool Ok { get; private set; } = true;

    public readonly int Remaining => _data.Length - Position;

    private bool Take(int count)
    {
        if (Ok && count >= 0 && count <= Remaining)
        {
            Position += count;
            return true;
        }

        Ok = false;
        return false;
    }

    private readonly ReadOnlySpan<byte> Last(int count) => _data.Slice(Position - count, count);

    public void Skip(int count) => Take(count);

    public void Seek(long position)
    {
        if (Ok && position >= 0 && position <= _data.Length)
            Position = (int)position;
        else
            Ok = false;
    }

    public ulong Unsigned(int size) => size switch
    {
        0 => 0,
        2 => UInt16() ?? 0,
        4 => UInt32() ?? 0,
        _ => UInt64() ?? 0
    };

    public byte? UInt8() => Take(1) ? _data[Position - 1] : null;

    public ushort? UInt16() => Take(2) ? UInt16(Last(2), littleEndian) : null;

    public uint? UInt32() => Take(4) ? UInt32(Last(4), littleEndian) : null;

    public int? Int32() => UInt32() is { } value ? (int)value : null;

    public ulong? UInt64() => Take(8) ? UInt64(Last(8), littleEndian) : null;
    
    public byte[]? Bytes(int count) => Take(count) ? Last(count).ToArray() : null;

    public static ushort UInt16(ReadOnlySpan<byte> b, bool little) => little ? BinaryPrimitives.ReadUInt16LittleEndian(b) : BinaryPrimitives.ReadUInt16BigEndian(b);

    public static uint UInt32(ReadOnlySpan<byte> b, bool little) => little ? BinaryPrimitives.ReadUInt32LittleEndian(b) : BinaryPrimitives.ReadUInt32BigEndian(b);

    public static ulong UInt64(ReadOnlySpan<byte> b, bool little) => little ? BinaryPrimitives.ReadUInt64LittleEndian(b) : BinaryPrimitives.ReadUInt64BigEndian(b);
}