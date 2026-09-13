using System.Reflection;
using Lyra.Imaging.Content;
using Lyra.Imaging.Metadata;
using Lyra.Imaging.Tests.Support;
using Xunit;

namespace Lyra.Imaging.Tests.Metadata;

/// <summary>
/// Metadata read from bytes already in hand must say the same thing as metadata read from the file.
/// </summary>
public class MetadataSourceParityTests
{
    private static readonly PropertyInfo[] Fields = typeof(ExifInfo).GetProperties(BindingFlags.Public | BindingFlags.Instance);

    /// <summary>Every field, not a chosen few: a mismatch anywhere is a regression.</summary>
    [Fact]
    public void AStreamAndAPathYieldTheSameMetadata()
    {
        var path = ExifJpegBuilder.Write(
            orientation: 6,
            dateTimeOriginal: "2024:09:26 18:32:17",
            artist: "Someone",
            xmp: new ExifJpegBuilder.XmpFields(Title: "A title", Description: "A description")
        );

        try
        {
            var fromPath = MetadataProcessor.ParseMetadata(path);

            using var stream = File.OpenRead(path);
            var fromStream = MetadataProcessor.ParseMetadata(stream, path);

            AssertSame(fromPath, fromStream);
        }
        finally
        {
            File.Delete(path);
        }
    }

    /// <summary>
    /// And from a buffer of the same bytes, which is what the decoders holding a whole file use.
    /// </summary>
    [Fact]
    public void ABufferAndAPathYieldTheSameMetadata()
    {
        var path = ExifJpegBuilder.Write(orientation: 3, dateTimeOriginal: "2020:01:02 03:04:05");

        try
        {
            var fromPath = MetadataProcessor.ParseMetadata(path);

            using var buffer = new MemoryStream(File.ReadAllBytes(path), writable: false);
            var fromBuffer = MetadataProcessor.ParseMetadata(buffer, path);

            AssertSame(fromPath, fromBuffer);
        }
        finally
        {
            File.Delete(path);
        }
    }

    /// <summary>
    /// Reading metadata leaves the stream usable, because the decode then rewinds and decodes from
    /// the same handle. A parser that closed it would break every stream-based decoder.
    /// </summary>
    [Fact]
    public void ParsingLeavesTheStreamOpenForTheDecodeThatFollows()
    {
        var path = ExifJpegBuilder.Write();

        try
        {
            using var stream = File.OpenRead(path);
            MetadataProcessor.ParseMetadata(stream, path);

            Assert.True(stream.CanRead && stream.CanSeek);

            stream.Position = 0;
            Assert.Equal(0xFF, stream.ReadByte());
            Assert.Equal(0xD8, stream.ReadByte()); // JPEG SOI, so the decoder still sees a JPEG.
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static void AssertSame(ExifInfo expected, ExifInfo actual)
    {
        var differences = Fields
            .Select(f => (f.Name, A: f.GetValue(expected)?.ToString() ?? "", B: f.GetValue(actual)?.ToString() ?? ""))
            .Where(x => x.A != x.B)
            .Select(x => $"{x.Name}: '{x.A}' vs '{x.B}'")
            .ToList();

        Assert.True(differences.Count == 0, string.Join("\n", differences));
    }
}