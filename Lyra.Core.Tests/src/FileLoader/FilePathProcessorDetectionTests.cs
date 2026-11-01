using Lyra.Common;
using Lyra.FileLoader.Enumeration;
using Xunit;

namespace Lyra.Core.Tests.FileLoader;

/// <summary>
/// Images whose names say nothing: opened when chosen directly, found in a folder when the name
/// leaves room for an image, and left alone otherwise.
/// </summary>
public sealed class FilePathProcessorDetectionTests : IDisposable
{
    private readonly string _folder = Directory.CreateTempSubdirectory("lyra-scan-").FullName;

    private static readonly byte[] Png = [0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 13];
    private static readonly byte[] Jpeg = [0xFF, 0xD8, 0xFF, 0xE0, 0, 16];
    private static readonly byte[] Mp4 = [0, 0, 0, 0x18, .. "ftypisom"u8, 0, 0, 2, 0, .. "isomiso2"u8];

    public void Dispose()
    {
        try
        {
            Directory.Delete(_folder, recursive: true);
        }
        catch (IOException) { }
    }

    private string Write(string name, byte[] bytes)
    {
        var path = Path.Combine(_folder, name);
        File.WriteAllBytes(path, bytes);
        return path;
    }

    private static Dictionary<string, ImageFormatType> Scan(params string[] paths) =>
        FilePathProcessor
            .ProcessImagePaths([.. paths], recurseSubdirs: null, TestContext.Current.CancellationToken)
            .Files
            .ToDictionary(record => record.Name, record => record.Format);

    private void WriteFolder()
    {
        Write("named.png", Png);
        Write("photo", Png);
        Write("download.jpg_large", Jpeg);
        Write("notes", "BM is not a bitmap here"u8.ToArray());
        Write("clip.mp4", Mp4);
        Write("hidden.xyz", Png);
    }

    [Fact]
    public void AFolderScanFindsImagesWhoseNamesLeaveRoomForOne()
    {
        WriteFolder();

        var found = Scan(_folder);

        Assert.Equal(
            new Dictionary<string, ImageFormatType>
            {
                ["named.png"] = ImageFormatType.Png,
                ["photo"] = ImageFormatType.Png,
                ["download.jpg_large"] = ImageFormatType.Jpeg
            },
            found);
    }

    [Fact]
    public void AFileOpenedDirectlyIsLookedInsideWhateverItsName()
    {
        WriteFolder();

        var found = Scan(Path.Combine(_folder, "hidden.xyz"));

        Assert.Equal(ImageFormatType.Png, found["hidden.xyz"]);
        Assert.Contains("named.png", found.Keys);
        Assert.DoesNotContain("clip.mp4", found.Keys);
    }

    [Fact]
    public void DroppedFilesThatAreNotImagesStayOut()
    {
        var notes = Write("notes.xyz", "plain text"u8.ToArray());
        var clip = Write("clip.mov", Mp4);
        var photo = Write("photo.bin", Png);

        var found = Scan(notes, clip, photo);

        Assert.Equal(["photo.bin"], found.Keys);
    }

    [Theory]
    [InlineData("", true)]
    [InlineData(".jpg_large", true)]
    [InlineData(".PNG~", true)]
    [InlineData(".jpeg1", true)]
    [InlineData(".jpgx", false)]
    [InlineData(".cs", false)]
    [InlineData(".mp4", false)]
    [InlineData(".html", false)]
    public void OnlyNamesThatLeaveRoomForAnImageAreLookedInside(string extension, bool candidate) => Assert.Equal(candidate, ImageFormat.MayBeMisnamed(extension));
}
