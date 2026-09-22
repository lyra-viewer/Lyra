using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
using Lyra.Imaging.Content;
using Lyra.Imaging.Metadata;
using Lyra.Imaging.Tests.Support;
using MetadataExtractor;
using MetadataExtractor.Formats.Png;
using Xunit;

namespace Lyra.Imaging.Tests.Metadata;

/// <summary>Faults that leave a PNG's metadata intact must not cost the file all of it.</summary>
public class PngChunkRepairTests
{
    [Fact]
    public void MetadataExtractorAlone_RejectsTheFile()
    {
        using var stream = new MemoryStream(Png("eXIf", "eXIf"));

        Assert.Throws<PngProcessingException>(() => ImageMetadataReader.ReadMetadata(stream));
    }

    [Fact]
    public void ARepeatedExifChunk_StillYieldsTheMetadata()
    {
        using var stream = new MemoryStream(Png("eXIf", "eXIf"));

        var info = MetadataProcessor.ParseMetadata(stream, "repeated-exif.png");

        Assert.Equal(ExifStatus.Ok, info.Status);
        Assert.Equal("Lyra", info.Make);
    }

    [Fact]
    public void Repair_KeepsTheFirstOfEachAndDropsThePixels()
    {
        using var stream = new MemoryStream(Png("eXIf", "eXIf"));

        using var repaired = PngChunkRepair.Repair(stream, out _);

        Assert.NotNull(repaired);
        Assert.Equal(["IHDR", "eXIf", "IEND"], ChunkTypes(repaired!.ToArray()));
    }

    [Fact]
    public void Repair_RefusesAFileWithNothingToDrop()
    {
        using var stream = new MemoryStream(Png("eXIf"));

        Assert.Null(PngChunkRepair.Repair(stream, out _));
    }

    [Fact]
    public void Repair_LeavesChunksThatMayRepeat()
    {
        using var stream = new MemoryStream(Png("tEXt", "tEXt"));

        Assert.Null(PngChunkRepair.Repair(stream, out _));
    }

    [Fact]
    public void MetadataExtractorAlone_RejectsAFileCutShort()
    {
        using var stream = new MemoryStream(CutInsidePixels(Png("eXIf")));

        Assert.ThrowsAny<IOException>(() => ImageMetadataReader.ReadMetadata(stream));
    }

    [Fact]
    public void AFileCutShort_StillYieldsTheMetadataBeforeTheCut()
    {
        using var stream = new MemoryStream(CutInsidePixels(Png("eXIf")));

        var info = MetadataProcessor.ParseMetadata(stream, "cut-short.png");

        Assert.Equal(ExifStatus.Ok, info.Status);
        Assert.Equal("Lyra", info.Make);
    }

    [Fact]
    public void Repair_EndsAtTheLastCompleteChunk()
    {
        using var stream = new MemoryStream(CutInsidePixels(Png("eXIf")));

        using var repaired = PngChunkRepair.Repair(stream, out var repair);

        Assert.NotNull(repaired);
        Assert.Equal(["IHDR", "eXIf", "IEND"], ChunkTypes(repaired!.ToArray()));
        Assert.Equal("cut short", repair);
    }

    [Fact]
    public void Repair_EndsBeforeAChunkLongerThanTheFile()
    {
        var png = Png("eXIf", "eXIf");
        BinaryPrimitives.WriteUInt32BigEndian(png.AsSpan(8 + 25), 0x7FFF_0000); // the first eXIf's length

        using var stream = new MemoryStream(png);
        using var repaired = PngChunkRepair.Repair(stream, out _);

        Assert.NotNull(repaired);
        Assert.Equal(["IHDR", "IEND"], ChunkTypes(repaired!.ToArray()));
    }

    [Fact]
    public void Repair_RefusesAFileCutInsideItsHeader()
    {
        using var stream = new MemoryStream(Png("eXIf")[..20]);

        Assert.Null(PngChunkRepair.Repair(stream, out _));
    }

    [Fact]
    public void Repair_RefusesSomethingElse()
    {
        using var stream = new MemoryStream(Encoding.ASCII.GetBytes("GIF89a, not a PNG at all"));

        Assert.Null(PngChunkRepair.Repair(stream, out _));
    }

    /// <summary>The file up to the middle of its first IDAT.</summary>
    private static byte[] CutInsidePixels(byte[] png)
    {
        var at = 8;
        while (Encoding.ASCII.GetString(png, at + 4, 4) != "IDAT")
            at += 12 + (int)BinaryPrimitives.ReadUInt32BigEndian(png.AsSpan(at));

        return png[..(at + 10)];
    }

    /// <summary>A 1x1 PNG with the given chunks between IHDR and IDAT.</summary>
    private static byte[] Png(params string[] extras)
    {
        using var png = new MemoryStream();
        png.Write(PngChunks.Signature);

        var header = new byte[13];
        BinaryPrimitives.WriteInt32BigEndian(header, 1);
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4), 1);
        header[8] = 8; // bit-depth
        header[9] = 0; // gray

        PngChunks.Write(png, "IHDR", header);

        foreach (var type in extras)
            PngChunks.Write(png, type, type == "eXIf" ? ExifWithMake("Lyra") : Encoding.Latin1.GetBytes("Comment\0text"));

        using (var compressed = new MemoryStream())
        {
            using (var zlib = new ZLibStream(compressed, CompressionLevel.Fastest, leaveOpen: true))
            {
                zlib.Write(new byte[2]);
            }

            PngChunks.Write(png, "IDAT", compressed.ToArray());
        }

        PngChunks.Write(png, "IEND", []);
        return png.ToArray();
    }

    /// <summary>A big-endian TIFF block holding one IFD0 entry, Make.</summary>
    private static byte[] ExifWithMake(string make)
    {
        var value = Encoding.ASCII.GetBytes(make + "\0");
        const int valueAt = 8 + 2 + 12 + 4;

        var exif = new byte[valueAt + value.Length];
        var span = exif.AsSpan();

        "MM"u8.CopyTo(span);
        BinaryPrimitives.WriteUInt16BigEndian(span[2..], 42);
        BinaryPrimitives.WriteUInt32BigEndian(span[4..], 8);
        BinaryPrimitives.WriteUInt16BigEndian(span[8..], 1);

        BinaryPrimitives.WriteUInt16BigEndian(span[10..], 0x010F); // Make
        BinaryPrimitives.WriteUInt16BigEndian(span[12..], 2);      // ASCII
        BinaryPrimitives.WriteUInt32BigEndian(span[14..], (uint)value.Length);
        BinaryPrimitives.WriteUInt32BigEndian(span[18..], valueAt);

        value.CopyTo(span[valueAt..]);
        return exif;
    }

    private static List<string> ChunkTypes(byte[] png)
    {
        var types = new List<string>();

        for (var at = 8; at + 12 <= png.Length; at += 12 + (int)BinaryPrimitives.ReadUInt32BigEndian(png.AsSpan(at)))
            types.Add(Encoding.ASCII.GetString(png, at + 4, 4));

        return types;
    }
}
