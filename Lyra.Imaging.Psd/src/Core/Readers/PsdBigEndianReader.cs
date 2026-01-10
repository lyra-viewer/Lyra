using System.Buffers.Binary;
using System.Text;
using Lyra.Imaging.Psd.Core.Primitives;

namespace Lyra.Imaging.Psd.Core.Readers;

public sealed class PsdBigEndianReader(Stream stream)
{
    public Stream BaseStream { get; } = stream ?? throw new ArgumentNullException(nameof(stream));
    
    #region Stream Helpers
    
    public bool CanSeek => BaseStream.CanSeek;
    
    public long Position
    {
        get => BaseStream.CanSeek ? BaseStream.Position : throw new NotSupportedException("Stream is not seekable.");
        set
        {
            if (!BaseStream.CanSeek)
                throw new NotSupportedException("Stream is not seekable.");

            BaseStream.Position = value;
        }
    }

    public long Length => BaseStream.CanSeek ? BaseStream.Length : throw new NotSupportedException("Stream does not support seeking.");

    #endregion
    
    #region Low-Level Primitives

    public byte ReadByte()
    {
        int b = BaseStream.ReadByte();
        if (b < 0) throw new EndOfStreamException();
        return (byte)b;
    }

    public void ReadExactly(Span<byte> buffer)
    {
        var total = 0;
        while (total < buffer.Length)
        {
            var n = BaseStream.Read(buffer[total..]);
            if (n == 0) throw new EndOfStreamException();
            total += n;
        }
    }

    public ushort ReadUInt16()
    {
        Span<byte> tmp = stackalloc byte[2];
        ReadExactly(tmp);
        return BinaryPrimitives.ReadUInt16BigEndian(tmp);
    }

    public short ReadInt16()
    {
        Span<byte> tmp = stackalloc byte[2];
        ReadExactly(tmp);
        return BinaryPrimitives.ReadInt16BigEndian(tmp);
    }

    public uint ReadUInt32()
    {
        Span<byte> tmp = stackalloc byte[4];
        ReadExactly(tmp);
        return BinaryPrimitives.ReadUInt32BigEndian(tmp);
    }

    public int ReadInt32()
    {
        Span<byte> tmp = stackalloc byte[4];
        ReadExactly(tmp);
        return BinaryPrimitives.ReadInt32BigEndian(tmp);
    }

    public ulong ReadUInt64()
    {
        Span<byte> tmp = stackalloc byte[8];
        ReadExactly(tmp);
        return BinaryPrimitives.ReadUInt64BigEndian(tmp);
    }

    public long ReadInt64()
    {
        Span<byte> tmp = stackalloc byte[8];
        ReadExactly(tmp);
        return BinaryPrimitives.ReadInt64BigEndian(tmp);
    }

    public void Skip(long bytes)
    {
        if (bytes < 0)
            throw new ArgumentOutOfRangeException(nameof(bytes));

        if (bytes == 0)
            return;

        if (BaseStream.CanSeek)
        {
            var newPos = BaseStream.Position + bytes;
            if (newPos < 0)
                throw new IOException("Stream seek overflow.");

            BaseStream.Position = newPos;
            return;
        }

        // Non-seekable: read & discard.
        Span<byte> scratch = stackalloc byte[4096];
        var remaining = bytes;
        while (remaining > 0)
        {
            var toRead = (int)Math.Min(scratch.Length, remaining);
            ReadExactly(scratch[..toRead]);
            remaining -= toRead;
        }
    }
    
    public bool TryPeekUInt32(out uint value)
    {
        value = 0;

        if (!CanSeek)
            return false;

        var pos = Position;
        value = ReadUInt32();
        Position = pos;
        return true;
    }
    
    #endregion

    #region PSD Helpers
    
    public void ExpectSignature(uint expectedFourCC)
    {
        var got = ReadUInt32();
        if (got != expectedFourCC)
            throw new InvalidDataException(
                $"Invalid signature. Expected '{FourCC.ToString(expectedFourCC)}' but got '{FourCC.ToString(got)}'.");
    }
    
    public bool TryPeekSignature(uint expected) => TryPeekUInt32(out var got) && got == expected;

    public string ReadPascalString(int padTo)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(padTo);

        var len = ReadByte();
        var data = new byte[len];
        ReadExactly(data);

        // Skip padding: total includes length byte
        var total = 1 + len;
        var padded = ((total + (padTo - 1)) / padTo) * padTo;
        Skip(padded - total);

        return Encoding.ASCII.GetString(data);
    }

    public string ReadUnicodeString()
    {
        var charCount = ReadInt32();
        if (charCount <= 0) return string.Empty;

        var byteCount = checked(charCount * 2);
        var bytes = new byte[byteCount];
        ReadExactly(bytes);

        // PSD uses big-endian UTF-16
        return Encoding.BigEndianUnicode.GetString(bytes);
    }

    #endregion
}