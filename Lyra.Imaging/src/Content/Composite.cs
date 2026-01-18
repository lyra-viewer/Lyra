using Lyra.Common;

namespace Lyra.Imaging.Content;

/// <summary>
/// Composite is the document shell: file identity, authoritative full dimensions, decode state,
/// and metadata. It owns the current decoded content representation (raster/vector/large).
/// </summary>
public sealed class Composite : IDisposable
{
    public Composite(FileInfo fileInfo)
    {
        FileInfo = fileInfo;
        ImageFormatType = ImageFormat.GetImageFormat(fileInfo.Extension);
    }

    // Common
    public FileInfo FileInfo { get; }
    public string? DecoderName;
    public ImageFormatType ImageFormatType { get; set; }
    public CompositeState State = CompositeState.Pending;

    // Authoritative document size (e.g. PSD full size when only preview is decoded)
    public float? FullWidth;
    public float? FullHeight;

    public double? LoadTimeElapsed;
    public double LoadTimeEstimated;

    // Content
    public ICompositeContent? Content;

    // Metadata
    public ExifInfo? ExifInfo;
    public readonly Dictionary<string, string> FormatSpecific = new();
    public bool IsGrayscale;

    // Derived sizes for UI/zoom/pan: always prefer Full dims, else fall back to best known dims from content.
    public float LogicalWidth  => FullWidth  ?? Content?.DecodedWidth  ?? 0f;
    public float LogicalHeight => FullHeight ?? Content?.DecodedHeight ?? 0f;

    public bool IsEmpty => Content is null;

    public void Dispose()
    {
        Content?.Dispose();
        Content = null;

        State = CompositeState.Disposed;
        GC.SuppressFinalize(this);
    }
}

public enum CompositeState
{
    Pending,
    Loading,
    Complete,
    Failed,
    Cancelled,
    Disposed
}