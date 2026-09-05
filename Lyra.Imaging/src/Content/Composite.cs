using System.Diagnostics;
using Lyra.Common;
using Lyra.Psd.Core.Decode.Layers;

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

    public double DecodeTimeEstimated;
    public long TransferBytesTotal;

    private long _transferBytesDone;
    private long _transferBytesLive;
    private long _transferMicroseconds;
    private int _transferReads;
    
    public long TransferBytesRead => Volatile.Read(ref _transferBytesDone) + Volatile.Read(ref _transferBytesLive);
    public bool TransferMeasured => Volatile.Read(ref _transferReads) > 0;
    
    public double ElapsedMs => _loadStopwatch?.Elapsed.TotalMilliseconds ?? 0;

    public double? TransferTimeMs => TransferMeasured
        ? Volatile.Read(ref _transferMicroseconds) / 1000.0
        : null;
    
    public double? DecodeTimeMs => LoadTimeComplete is { } total && TransferTimeMs is { } transfer
        ? Math.Max(0, total - transfer)
        : null;

    public event Action<Composite>? Completed;
    public event Action<Composite>? ProgressChanged;
    
    internal Task BackgroundDecodeTask = Task.CompletedTask;

    // Content
    public ICompositeContent? Content;

    // Written by a decoder thread and read by the UI thread; volatile so the record's
    // contents are guaranteed visible to the reader before the reference is.
    public volatile ExifInfo? ExifInfo;
    
    public volatile ExifOrientation AppliedOrientation = ExifOrientation.Normal;
    
    private readonly Dictionary<string, string> _formatSpecific = new();
    private readonly Lock _formatSpecificLock = new();

    public IReadOnlyList<StructureGroup>? Structure;
    public LayerRecord[]? PsdLayers;
    
    /// <summary>
    /// Why the scene-referred half-float form was not kept, or null when it was and for everything
    /// that was never HDR. Written by a decoder thread, read by the UI thread.
    /// </summary>
    public volatile string? HdrBakedReason;
    
    public bool IsHdrImage => IsHdrDecoded || HdrBakedReason is not null;

    /// <summary>
    /// Whether the pixels are still scene-referred, and so whether exposure and curve apply at
    /// draw time.
    /// </summary>
    public bool IsHdrDecoded => Content is HdrRasterContent or RasterLargeContent { HasScenePreview: true };

    // Derived sizes for UI/zoom/pan: always prefer Full dims, else fall back to best known dims from content.
    public float LogicalWidth => FullWidth ?? Content?.DecodedWidth ?? 0f;
    public float LogicalHeight => FullHeight ?? Content?.DecodedHeight ?? 0f;

    public bool IsEmpty => Content is null;

    /// <summary>Records a format-specific metadata entry. Safe to call from a decoder worker thread.</summary>
    public void AddFormatSpecific(string key, string value)
    {
        lock (_formatSpecificLock)
        {
            _formatSpecific[key] = value;
        }
    }

    /// <summary>A consistent snapshot of the format-specific metadata for the UI to render.</summary>
    public List<KeyValuePair<string, string>> FormatSpecificSnapshot()
    {
        lock (_formatSpecificLock)
        {
            return _formatSpecific.ToList();
        }
    }

    /// <summary>
    /// Reports how far the read currently in flight has got, for a progress bar to follow. Called
    /// from a decoder thread.
    /// </summary>
    internal void ReportTransferred(long bytesSoFar) => Volatile.Write(ref _transferBytesLive, bytesSoFar);
    
    internal void CompleteTransfer(long bytes, double ms)
    {
        Interlocked.Add(ref _transferBytesDone, bytes);
        Volatile.Write(ref _transferBytesLive, 0);
        Interlocked.Add(ref _transferMicroseconds, (long)(ms * 1000));
        Interlocked.Increment(ref _transferReads);
    }

    internal void BeginLoadTiming()
    {
        _loadStopwatch = Stopwatch.StartNew();
        _readySignaled = 0;
        LoadTimeReady = null;
        LoadTimeComplete = null;

        Volatile.Write(ref _transferBytesDone, 0);
        Volatile.Write(ref _transferBytesLive, 0);
        Volatile.Write(ref _transferMicroseconds, 0);
        Volatile.Write(ref _transferReads, 0);
    }

    internal void SignalReady()
    {
        if (Interlocked.Exchange(ref _readySignaled, 1) != 0)
            return;

        if (_loadStopwatch is { IsRunning: true })
            LoadTimeReady = _loadStopwatch.Elapsed.TotalMilliseconds;

        if (State == CompositeState.Loading)
            State = CompositeState.Ready;

        ProgressChanged?.Invoke(this);
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

        ProgressChanged?.Invoke(this);
        Completed?.Invoke(this);
    }

    internal void SignalProgress() => ProgressChanged?.Invoke(this);

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
    Ready,      // preview / full usable (tiles may still stream)
    Complete,   // everything finished (e.g., tiles fully decoded)
    Failed,
    Cancelled,
    Disposed
}