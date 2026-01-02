using System.Buffers.Binary;
using System.Text;

namespace Lyra.Imaging.Psd.Parser.PsdReader;

internal sealed class PsdBigEndianReader(Stream stream)
{
    private readonly Stream _stream = stream ?? throw new ArgumentNullException(nameof(stream));

    #region Stream Helpers

    public bool CanSeek => _stream.CanSeek;

    public long Position
    {
        get => _stream.CanSeek ? _stream.Position : throw new NotSupportedException("Stream is not seekable.");
        set
        {
            if (!_stream.CanSeek)
                throw new NotSupportedException("Stream is not seekable.");

            _stream.Position = value;
        }
    }

    public long Length => _stream.CanSeek ? _stream.Length : throw new NotSupportedException("Stream does not support seeking.");

    #endregion
    
    #region Low-Level Primitives

    public byte ReadByte()
    {
        int b = _stream.ReadByte();
        if (b < 0) throw new EndOfStreamException();
        return (byte)b;
    }

    public void ReadExactly(Span<byte> buffer)
    {
        var total = 0;
        while (total < buffer.Length)
        {
            var n = _stream.Read(buffer[total..]);
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

        if (_stream.CanSeek)
        {
            var newPos = _stream.Position + bytes;
            if (newPos < 0)
                throw new IOException("Stream seek overflow.");

            _stream.Position = newPos;
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

    #endregion

    #region PSD Helpers

    public void ExpectSignature(ReadOnlySpan<byte> expected)
    {
        if (expected.Length is 0 or > 255)
            throw new ArgumentOutOfRangeException(nameof(expected), "Invalid expected signature length.");

        Span<byte> got = stackalloc byte[expected.Length];
        ReadExactly(got);
        if (!got.SequenceEqual(expected))
            throw new InvalidDataException("Invalid signature.");
    }

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