using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;

namespace Lyra.Metadata.Tests.Support;

/// <summary>Builds PNG, WebP and GIF files from their chunks and blocks, with no image anyone could decode.</summary>
internal static class ChunkFixture
{
    // PNG

    public static byte[] Png(params byte[][] chunks) => [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, .. chunks.SelectMany(chunk => chunk), .. Chunk("IEND", [])];

    public static byte[] Chunk(string type, byte[] data)
    {
        byte[] typed = [.. Encoding.ASCII.GetBytes(type), .. data];
        var length = new byte[4];
        var crc = new byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(length, (uint)data.Length);
        BinaryPrimitives.WriteUInt32BigEndian(crc, Crc32(typed));
        return [.. length, .. typed, .. crc];
    }

    public static byte[] Ihdr(byte depth = 8, byte colorType = 2) => Chunk("IHDR", [0, 0, 0, 8, 0, 0, 0, 8, depth, colorType, 0, 0, 0]);

    public static byte[] Iccp(string name, byte[] profile) => Chunk("iCCP", [.. Encoding.Latin1.GetBytes(name), 0, 0, .. Zlib(profile)]);

    public static byte[] ITxt(string keyword, string text, bool compressed = false) =>
        Chunk("iTXt", [.. Encoding.ASCII.GetBytes(keyword), 0, compressed ? (byte)1 : (byte)0, 0, 0, 0, .. compressed ? Zlib(Encoding.UTF8.GetBytes(text)) : Encoding.UTF8.GetBytes(text)]);

    public static byte[] ZTxt(string keyword, string text) => Chunk("zTXt", [.. Encoding.ASCII.GetBytes(keyword), 0, 0, .. Zlib(Encoding.UTF8.GetBytes(text))]);

    public static byte[] TExt(string keyword, string text) => Chunk("tEXt", [.. Encoding.ASCII.GetBytes(keyword), 0, .. Encoding.Latin1.GetBytes(text)]);

    /// <summary>ImageMagick's raw profile text: a newline, the name, the length, then hex wrapped at 72 characters.</summary>
    public static string RawProfile(string name, byte[] data)
    {
        var hex = Convert.ToHexString(data).ToLowerInvariant();
        var lines = string.Join("\n", hex.Chunk(72).Select(chunk => new string(chunk)));
        return $"\n{name}\n{data.Length,8}\n{lines}\n";
    }

    public static byte[] Zlib(byte[] data)
    {
        using var output = new MemoryStream();
        using (var zlib = new ZLibStream(output, CompressionLevel.Fastest))
            zlib.Write(data);

        return output.ToArray();
    }

    private static uint Crc32(byte[] data)
    {
        var crc = 0xFFFFFFFFu;
        foreach (var b in data)
        {
            crc ^= b;
            for (var bit = 0; bit < 8; bit++)
                crc = (crc & 1) != 0 ? 0xEDB88320u ^ (crc >> 1) : crc >> 1;
        }

        return crc ^ 0xFFFFFFFFu;
    }

    // WebP

    public static byte[] Webp(params byte[][] chunks)
    {
        byte[] body = [.. "WEBP"u8, .. chunks.SelectMany(chunk => chunk)];
        var size = new byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(size, (uint)body.Length);
        return [.. "RIFF"u8, .. size, .. body];
    }

    public static byte[] RiffChunk(string fourCc, byte[] data)
    {
        var size = new byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(size, (uint)data.Length);
        byte[] chunk = [.. Encoding.ASCII.GetBytes(fourCc), .. size, .. data];
        return data.Length % 2 == 0 ? chunk : [.. chunk, 0];
    }

    public static byte[] Vp8L => RiffChunk("VP8L", [0x2F, 7, 0xC0, 0x01, 0, 0, 0, 0]);

    // GIF

    public static byte[] Gif(params byte[][] blocks) => [.. "GIF89a"u8, 8, 0, 8, 0, 0, 0, 0, .. blocks.SelectMany(block => block), 0x3B];

    public static byte[] GifImage => [0x2C, 0, 0, 0, 0, 8, 0, 8, 0, 0, 2, 2, 0x44, 0x01, 0];

    public static byte[] SubBlocks(byte[] data) => [.. data.Chunk(255).SelectMany(chunk => (byte[])[(byte)chunk.Length, .. chunk]), 0];

    public static byte[] GifXmp(string packet) => [0x21, 0xFF, 11, .. "XMP DataXMP"u8, .. Encoding.UTF8.GetBytes(packet), 1, .. Enumerable.Range(0, 256).Select(i => (byte)(255 - i)), 0];

    public static byte[] GifIcc(byte[] profile) => [0x21, 0xFF, 11, .. "ICCRGBG1012"u8, .. SubBlocks(profile)];

    public static byte[] GifComment(string text) => [0x21, 0xFE, .. SubBlocks(Encoding.ASCII.GetBytes(text))];
}