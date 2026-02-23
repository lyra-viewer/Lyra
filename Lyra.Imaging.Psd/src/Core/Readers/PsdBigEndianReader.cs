using System.Buffers.Binary;
using System.Text;
using Lyra.Imaging.Psd.Core.Common;

namespace Lyra.Imaging.Psd.Core.Readers;

public sealed class PsdBigEndianReader(Stream stream)
{
    public Stream BaseStream { get; } = stream ?? throw new ArgumentNullException(nameof(stream));

    // Reusable scratch avoids repeated stackalloc in hot paths.
    private readonly byte[] _scratch8 = new byte[8];

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

    public void ReadExactly(Span<byte> buffer) => BaseStream.ReadExactly(buffer);

    public byte ReadByte()
    {
        var b = BaseStream.ReadByte();
        if (b < 0) 
            throw new EndOfStreamException();
        
        return (byte)b;
    }

    public ushort ReadUInt16()
    {
        ReadExactly(_scratch8.AsSpan(0, 2));
        return BinaryPrimitives.ReadUInt16BigEndian(_scratch8.AsSpan(0, 2));
    }

    public short ReadInt16()
    {
        ReadExactly(_scratch8.AsSpan(0, 2));
        return BinaryPrimitives.ReadInt16BigEndian(_scratch8.AsSpan(0, 2));
    }

    public uint ReadUInt32()
    {
        ReadExactly(_scratch8.AsSpan(0, 4));
        return BinaryPrimitives.ReadUInt32BigEndian(_scratch8.AsSpan(0, 4));
    }

    public int ReadInt32()
    {
        ReadExactly(_scratch8.AsSpan(0, 4));
        return BinaryPrimitives.ReadInt32BigEndian(_scratch8.AsSpan(0, 4));
    }

    public ulong ReadUInt64()
    {
        ReadExactly(_scratch8.AsSpan(0, 8));
        return BinaryPrimitives.ReadUInt64BigEndian(_scratch8.AsSpan(0, 8));
    }

    public byte[] ReadBytes(int count)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        if (count == 0)
            return [];

        var data = new byte[count];
        ReadExactly(data);
        return data;
    }

    public void Skip(long bytes)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(bytes);

        if (bytes == 0)
            return;

        if (BaseStream.CanSeek)
        {
            BaseStream.Seek(bytes, SeekOrigin.Current);
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
            throw new InvalidDataException($"Invalid signature. Expected '{FourCC.ToString(expectedFourCC)}' but got '{FourCC.ToString(got)}'.");
    }

    public bool TryPeekSignature(uint expected) => TryPeekUInt32(out var got) && got == expected;

    public string ReadPascalString(int padTo)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(padTo);

        var len = ReadByte();
        var data = ReadBytes(len);

        var total = 1 + len;
        var padded = ((total + (padTo - 1)) / padTo) * padTo;
        Skip(padded - total);

        return Encoding.ASCII.GetString(data);
    }

    public string ReadUnicodeString()
    {
        var charCount = ReadInt32();
        if (charCount == 0)
            return string.Empty;

        if (charCount < 0)
            throw new InvalidDataException($"Invalid unicode string length: {charCount}.");

        var byteCount = checked(charCount * 2);
        var bytes = ReadBytes(byteCount);

        // PSD uses big-endian UTF-16
        return Encoding.BigEndianUnicode.GetString(bytes);
    }

    #endregion
}