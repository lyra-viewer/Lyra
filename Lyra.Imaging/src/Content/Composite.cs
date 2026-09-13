using Lyra.Common;
using Lyra.Psd.Core.Decode.Layers;

namespace Lyra.Imaging.Content;

/// <summary>
/// Composite is the document shell: file identity, authoritative full dimensions, decode state,
/// and metadata. It owns the current decoded content representation (raster/vector/large), and
/// <see cref="Timing"/> holds what the load cost.
/// </summary>
public sealed class Composite : IDisposable
{
    public Composite(FileInfo fileInfo)
    {
        FileInfo = fileInfo;
        ImageFormatType = ImageFormat.GetImageFormat(fileInfo.Extension);
        FileSizeBytes = ReadSize(fileInfo);
    }

    // Common
    public FileInfo FileInfo { get; }

    public long? FileSizeBytes { get; }

    public string? DecoderName;
    public ImageFormatType ImageFormatType { get; set; }

    // Authoritative document size (e.g. PSD full size when only preview is decoded)
    public float? FullWidth;
    public float? FullHeight;

    public LoadMeasurement Timing { get; } = new();
    
    public long? PixelCount => Volatile.Read(ref _pixelCount) is var pixels and > 0 ? pixels : null;

    private long _pixelCount;

    public CompositeState State = CompositeState.Pending;

    public event Action<Composite>? Completed;
    public event Action<Composite>? ProgressChanged;
    internal event Action<Composite>? PixelCountReported;

    /// <summary>
    /// A decode that outlives the call that started it - the PSD layer pass is the one - so that
    /// the loader can defer disposing the composite until it has finished with it.
    /// </summary>
    internal Task BackgroundDecodeTask = Task.CompletedTask;

    private int _readySignaled;
    private int _completeSignaled;

    public volatile ICompositeContent? Content;

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

    public float LogicalWidth => Content is VariantRasterContent pages
        ? pages.DecodedWidth ?? 0f
        : FullWidth ?? Content?.DecodedWidth ?? 0f;

    public float LogicalHeight => Content is VariantRasterContent pages
        ? pages.DecodedHeight ?? 0f
        : FullHeight ?? Content?.DecodedHeight ?? 0f;

    public bool IsEmpty => Content is null;

    private static long? ReadSize(FileInfo fileInfo)
    {
        try
        {
            return fileInfo.Length;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Logger.Warning($"[Composite] Could not read the size of '{fileInfo.FullName}': {ex.Message}");
            return null;
        }
    }

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
    
    public void ReportPixelCount(long width, long height)
    {
        if (width <= 0 || height <= 0)
            return;

        var pixels = width * height;
        if (Interlocked.Exchange(ref _pixelCount, pixels) == pixels)
            return;

        PixelCountReported?.Invoke(this);
    }

    /// <inheritdoc cref="LoadMeasurement.ReportTransferred"/>
    internal void ReportTransferred(long bytesSoFar) => Timing.ReportTransferred(bytesSoFar);

    /// <inheritdoc cref="LoadMeasurement.CompleteTransfer"/>
    internal void CompleteTransfer(long bytes, double ms) => Timing.CompleteTransfer(bytes, ms);

    /// <summary>Starts the clock, and re-arms the signals that stop it.</summary>
    internal void BeginLoadTiming()
    {
        Interlocked.Exchange(ref _readySignaled, 0);
        Interlocked.Exchange(ref _completeSignaled, 0);

        Timing.Begin();
    }

    internal void SignalReady()
    {
        if (Interlocked.Exchange(ref _readySignaled, 1) != 0)
            return;

        Timing.MarkReady();

        if (State == CompositeState.Loading)
            State = CompositeState.Ready;

        ProgressChanged?.Invoke(this);
    }

    internal void SignalComplete()
    {
        if (Interlocked.Exchange(ref _completeSignaled, 1) != 0)
            return;

        Timing.MarkComplete();

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
    }
}