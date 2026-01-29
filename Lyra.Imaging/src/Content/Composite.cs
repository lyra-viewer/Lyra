using System.Diagnostics;
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

    public double? LoadTimeReady;
    public double? LoadTimeComplete;

    private Stopwatch? _loadStopwatch;
    private int _readySignaled;
    private int _completeSignaled;
    public double LoadTimeEstimated;
    
    public event Action<Composite>? Completed;

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

    internal void BeginLoadTiming()
    {
        _loadStopwatch = Stopwatch.StartNew();
        _readySignaled = 0;
        LoadTimeReady = null;
        LoadTimeComplete = null;
    }

    internal void SignalReady()
    {
        if (Interlocked.Exchange(ref _readySignaled, 1) != 0)
            return;

        if (_loadStopwatch is { IsRunning: true })
            LoadTimeReady = _loadStopwatch.Elapsed.TotalMilliseconds;

        if (State == CompositeState.Loading)
            State = CompositeState.Ready;
    }

    internal void SignalComplete()
    {
        if (Interlocked.Exchange(ref _completeSignaled, 1) != 0)
            return;

        if (_loadStopwatch is { IsRunning: true })
        {
            _loadStopwatch.Stop();
            LoadTimeComplete = _loadStopwatch.Elapsed.TotalMilliseconds;
        }

        if (State is CompositeState.Loading or CompositeState.Ready)
            State = CompositeState.Complete;

        Completed?.Invoke(this);
    }

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
    Ready,     // preview / full usable (tiles may still stream)
    Complete,  // everything finished (e.g., tiles fully decoded)
    Failed,
    Cancelled,
    Disposed
}