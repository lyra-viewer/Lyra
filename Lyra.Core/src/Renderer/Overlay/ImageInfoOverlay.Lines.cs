using Lyra.Common.SystemExtensions;
using Lyra.Imaging.Content;

namespace Lyra.Renderer.Overlay;

public partial class ImageInfoOverlay
{
    private static List<string> BuildLines(Composite composite, ViewerState states)
    {
        var fileInfo = composite.FileInfo;
        var fileSize = SizeToStr(fileInfo.Length);

        var width = composite.LogicalWidth;
        var height = composite.LogicalHeight;

        var dirNav = string.Empty;
        if (states is { DirectoryCount: not null, DirectoryIndex: not null })
            dirNav = $" <e>({states.DirectoryIndex}/{states.DirectoryCount})</>";

        var lines = new List<string>
        {
            $"[Collection]    {states.CollectionType}  |  Dir: {composite.FileInfo.DirectoryName}/",
            $"[File]          {states.CollectionIndex}/{states.CollectionCount}{dirNav}  |  {fileInfo.Name}  |  {fileSize}",
            $"[Image]         {composite.ImageFormatType.Description()}  |  {width}x{height}" + (composite.IsGrayscale ? "  |  Greyscale" : ""),
            $"[Displaying]    Zoom: {states.Zoom}%  |  Display Mode: {states.DisplayMode}",
            $"[System]        Graphics API: OpenGL  |  Sampling: {states.SamplingMode}"
        };

#if DEBUG
        lines.AddRange(BuildDebugLines(composite));
#endif

        if (states.ShowExif)
        {
            lines.AddRange(BuildFormatSpecificLines(composite));
            lines.AddRange(BuildExifLines(composite));
        }

        return lines;
    }

    private static IEnumerable<string> BuildDebugLines(Composite composite)
    {
        yield return "";
        yield return "";
        yield return "";
        yield return "<d>[Debug]</>";
        yield return "";
        yield return $"<d>[State]         {composite.State.Description()}</>";
        yield return $"<d>[Decoder]       {composite.DecoderName}</>";
        yield return $"<d>[Time (ms)]     Estimated: {MsToStr(composite.LoadTimeEstimated)}  |  Elapsed: {MsToStr(composite.LoadTimeComplete)}</>";
    }

    private static IEnumerable<string> BuildFormatSpecificLines(Composite composite)
    {
        if (composite.FormatSpecific.Count == 0)
        {
            yield break;
        }
        
        yield return "";
        yield return "";
        yield return "";
        foreach (var line in composite.FormatSpecific)
            yield return $"{line.Key}: {line.Value}";
    }

    private static IEnumerable<string> BuildExifLines(Composite composite)
    {
        yield return "";
        yield return "";
        yield return "";
        yield return "[EXIF ↯]";
        yield return "";

        if (composite.ExifInfo == null)
        {
            yield return "<f>No EXIF metadata</>";
        }
        else if (!composite.ExifInfo.IsValid())
        {
            yield return "<f>Failed to parse EXIF metadata.</>";
        }
        else if (!composite.ExifInfo.HasData())
        {
            yield return "<f>No recognized EXIF metadata</>";
        }
        else
        {
            foreach (var line in composite.ExifInfo.ToLines())
                yield return line;
        }
    }

    private static string SizeToStr(long fileSize)
    {
        return fileSize switch
        {
            >= 2 * 1024 * 1024 => $"{Math.Round(fileSize / (1024.0 * 1024), 1)} MB",
            >= 1024 => $"{Math.Round(fileSize / 1024.0)} kB",
            _ => $"{fileSize} bytes"
        };
    }

    private static string MsToStr(double? ms)
    {
        return ms switch
        {
            null => "n/a",
            < 10 => ms.Value.ToString("0.00"),
            _ => ms.Value.ToString("0")
        };
    }
}