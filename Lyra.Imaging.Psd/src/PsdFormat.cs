using SixLabors.ImageSharp.Formats;

namespace Lyra.Imaging.Psd;

public sealed class PsdFormat : IImageFormat
{
    public static PsdFormat Instance { get; } = new();

    public string Name => "PSD";

    public string DefaultMimeType => "image/vnd.adobe.photoshop";

    public IEnumerable<string> MimeTypes =>
    [
        "image/vnd.adobe.photoshop",
        "image/x-photoshop",
        "application/x-photoshop"
    ];

    public IEnumerable<string> FileExtensions => ["psd", "psb"];
}