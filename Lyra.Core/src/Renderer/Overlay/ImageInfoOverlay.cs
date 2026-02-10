using Lyra.Common.SystemExtensions;
using Lyra.Imaging.Content;
using Lyra.SdlCore;
using SkiaSharp;

namespace Lyra.Renderer.Overlay;

public class ImageInfoOverlay : IOverlay<(Composite? composite, ApplicationStates states)>
{
    public float Scale { get; set; }
    public SKFont? Font { get; set; }

    private readonly TaggedTextRenderer _text = new();

    public ImageInfoOverlay()
    {
        ReloadFont();
    }

    public void ReloadFont()
    {
        Font = FontHelper.GetScaledMonoFont(14, Scale);
    }

    public void Render(SKCanvas canvas, DrawableBounds drawableBounds, SKColor textPaint, (Composite? composite, ApplicationStates states) data)
    {
        if (Font == null || data.composite == null)
            return;

        _text.SetTextColor(textPaint);

        var padding = OverlayTextMetrics.Padding(Scale);
        var lineHeight = OverlayTextMetrics.LineHeight(Font, Scale);
        var textY = padding + Font.Size;

        foreach (var line in BuildLines(data.composite, data.states))
        {
            _text.Draw(canvas, line, padding, textY, Font);
            textY += lineHeight;
        }
    }

    private static List<string> BuildLines(Composite composite, ApplicationStates states)
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
        lines.AddRange(BuildDebugLines(composite, states));
#endif

        if (states.ShowExif)
        {
            lines.AddRange(BuildFormatSpecificLines(composite));
            lines.AddRange(BuildExifLines(composite));
        }

        return lines;
    }

    private static IEnumerable<string> BuildDebugLines(Composite composite, ApplicationStates states)
    {
        var dropStatus = states.DropAborted ? "Aborted  |  " : (states.DropActive ? "Active  |  " : "");

        yield return "";
        yield return "";
        yield return "";
        yield return "<d>[Debug]</>";
        yield return "";
        yield return $"<d>[State]         {composite.State.Description()}</>";
        yield return $"<d>[Decoder]       {composite.DecoderName}</>";
        yield return $"<d>[Time (ms)]     Estimated: {MsToStr(composite.LoadTimeEstimated)}  |  Elapsed: {MsToStr(composite.LoadTimeComplete)}</>";
        yield return "";
        yield return $"<d>[Drag & Drop]   {dropStatus}Paths Enqueued: {states.DropPathsEnqueued}  |  All Files: {states.DropFilesEnumerated}  |  Supported: {states.DropFilesSupported}</>";
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

    private static string SizeToStr(long bytes)
    {
        const long kB = 1024;
        const long MB = kB * 1024;

        return bytes switch
        {
            >= 100 * MB => $"{bytes / MB} MB",
            >= 2 * MB => $"{Math.Round(bytes / (double)MB, 1)} MB",
            >= kB => $"{bytes / kB} kB",
            _ => $"{bytes} bytes"
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