using Lyra.Common;
using Lyra.Imaging.Content.Exif;
using Lyra.Imaging.Content.Hdr;
using Lyra.Imaging.Content.Status;
using Lyra.Imaging.Content.Textures;
using Lyra.Imaging.Content.Variants;
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

    /// <summary>
    /// An image held in memory, such as a texture inside a 3D model. <paramref name="fileInfo"/>
    /// names it and is never opened; the decoders read <see cref="Payload"/> instead.
    /// </summary>
    internal Composite(FileInfo fileInfo, ImageFormatType format, byte[] payload)
    {
        FileInfo = fileInfo;
        ImageFormatType = format;
        Payload = payload;
        FileSizeBytes = payload.LongLength;
    }

    public FileInfo FileInfo { get; }

    /// <summary>The image's bytes when it is not a file of its own.</summary>
    internal byte[]? Payload { get; }

    /// <summary>The file's first bytes, as read when its format was detected; null until then.</summary>
    internal byte[]? Head { get; set; }

    public long? FileSizeBytes { get; }

    private volatile string? _decoderName;

    public string? DecoderName
    {
        get => _decoderName;
        internal set => _decoderName = value;
    }

    private volatile ImageFormatType _imageFormatType;

    public ImageFormatType ImageFormatType
    {
        get => _imageFormatType;
        internal set => _imageFormatType = value;
    }

    private sealed record FullSize(float Width, float Height);

    private volatile FullSize? _fullSize;

    /// <summary>The document's own size, when the content holds less of it (e.g. a PSD's preview).</summary>
    public float? FullWidth => _fullSize?.Width;

    /// <inheritdoc cref="FullWidth"/>
    public float? FullHeight => _fullSize?.Height;

    internal void SetFullSize(float width, float height) => _fullSize = new FullSize(width, height);

    public LoadMeasurement Timing { get; } = new();

    public long? PixelCount => Volatile.Read(ref _pixelCount) is var pixels and > 0 ? pixels : null;

    private long _pixelCount;

    private int _state = (int)CompositeState.Pending;

    public CompositeState State => (CompositeState)Volatile.Read(ref _state);

    internal static bool CanMove(CompositeState from, CompositeState to) => from switch
    {
        CompositeState.Pending => to is CompositeState.Loading or CompositeState.Failed or CompositeState.Cancelled or CompositeState.Disposed,
        CompositeState.Loading => to is CompositeState.Ready or CompositeState.Complete or CompositeState.Failed or CompositeState.Cancelled or CompositeState.Disposed,
        CompositeState.Ready => to is CompositeState.Complete or CompositeState.Failed or CompositeState.Cancelled or CompositeState.Disposed,
        CompositeState.Complete or CompositeState.Failed or CompositeState.Cancelled => to is CompositeState.Disposed,
        _ => false
    };

    internal bool TryTransition(CompositeState to)
    {
        while (true)
        {
            var from = State;
            if (!CanMove(from, to))
                return false;

            if (TryTransition(from, to))
                return true;
        }
    }

    internal bool TryTransition(CompositeState from, CompositeState to) => CanMove(from, to) && Interlocked.CompareExchange(ref _state, (int)to, (int)from) == (int)from;

    private volatile LoadFailure? _failure;

    /// <summary>Why the load failed; set before <see cref="State"/> reads Failed.</summary>
    public LoadFailure? Failure => _failure;

    /// <summary>False when the composite has already finished, failed or gone.</summary>
    internal bool Fail(LoadFailure failure)
    {
        if (Interlocked.CompareExchange(ref _failure, failure, null) is not null)
            return false;

        if (TryTransition(CompositeState.Failed))
            return true;

        Interlocked.CompareExchange(ref _failure, null, failure);
        return false;
    }

    public event Action<Composite>? Completed;
    public event Action<Composite>? ProgressChanged;
    internal event Action<Composite>? PixelCountReported;
    
    internal volatile Task BackgroundDecodeTask = Task.CompletedTask;

    /// <summary>The queued task that starts <see cref="BackgroundDecodeTask"/>, which the loader promotes.</summary>
    internal volatile Task BackgroundDecodeStart = Task.CompletedTask;

    /// <summary>Whether this is the image on screen; set by the loader. Its work goes ahead of preloads.</summary>
    internal volatile bool IsCurrent;

    private int _readySignaled;
    private int _completeSignaled;

    public volatile ICompositeContent? Content;

    // Written by a decoder thread and read by the UI thread; volatile so the record's
    // contents are guaranteed visible to the reader before the reference is.
    public volatile ExifInfo? ExifInfo;

    public volatile ExifOrientation AppliedOrientation = ExifOrientation.Normal;

    /// <summary>
    /// A status line for work that keeps running after the first image is on screen, such as a raw
    /// developing behind its preview; once the work is done, it says how it went. Null when there
    /// is nothing to report. Set from the decoder thread, which signals progress after each change.
    /// </summary>
    public volatile LoadStage? Stage;

    /// <summary>
    /// True while a preview stands in for a scene-referred image that is still on its way, so the
    /// HDR controls show up right away instead of popping in when the image arrives.
    /// </summary>
    public volatile bool HdrPending;

    /// <summary>
    /// Set by a decoder when the image is shown, but not whole. A set of renditions keeps one per
    /// rendition instead, on <see cref="VariantContainer.WarningOf"/>.
    /// </summary>
    public volatile LoadWarning? Warning;

    private readonly Dictionary<string, string> _formatSpecific = new();
    private readonly Lock _formatSpecificLock = new();
    private int _formatSpecificVersion;

    public volatile IReadOnlyList<StructureGroup>? Structure;
    public volatile LayerRecord[]? PsdLayers;

    /// <summary>
    /// Why the scene-referred half-float form was not kept, or null when it was and for everything
    /// that was never HDR. Written by a decoder thread, read by the UI thread.
    /// </summary>
    public volatile string? HdrBakedReason;

    public bool IsHdrImage => IsHdrDecoded || HdrBakedReason is not null;

    /// <summary>Whether the pixels are still scene-referred, and so whether exposure and curve apply at draw time.</summary>
    public bool IsHdrDecoded => Displayed is HdrRasterContent or RasterLargeContent { HasScenePreview: true } or ContactSheetContent { HasHdrCells: true };

    /// <summary>What the image on screen says about where its light ends, and its own curve.</summary>
    public SceneRange DisplayedRange => Displayed switch
    {
        HdrRasterContent hdr => hdr.Range,
        RasterLargeContent large => large.Range,
        _ => default
    };

    /// <summary>What is on screen: the content itself, or the rendition a page set is showing.</summary>
    private ICompositeContent? Displayed => Content is VariantContainer variants ? variants.Active : Content;

    public float LogicalWidth => Content is VariantContainer pages
        ? pages.DecodedWidth ?? 0f
        : FullWidth ?? Content?.DecodedWidth ?? 0f;

    public float LogicalHeight => Content is VariantContainer pages
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
            if (_formatSpecific.TryGetValue(key, out var existing) && existing == value)
                return;

            _formatSpecific[key] = value;
            _formatSpecificVersion++;
        }
    }

    /// <summary>Records a yes-or-no format-specific entry.</summary>
    public void AddFormatSpecific(string key, bool value) => AddFormatSpecific(key, value ? "Yes" : "No");

    /// <summary>Changes whenever an entry is added or its value changes, so the UI knows to redraw.</summary>
    public int FormatSpecificVersion
    {
        get
        {
            lock (_formatSpecificLock)
                return _formatSpecificVersion;
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

        TryTransition(CompositeState.Loading, CompositeState.Ready);

        ProgressChanged?.Invoke(this);
    }

    internal void SignalComplete()
    {
        if (Interlocked.Exchange(ref _completeSignaled, 1) != 0)
            return;

        Timing.MarkComplete();

        TryTransition(CompositeState.Complete);

        ProgressChanged?.Invoke(this);
        Completed?.Invoke(this);
    }

    internal void SignalProgress() => ProgressChanged?.Invoke(this);

    /// <summary>Runs once. The content is taken off the composite before it is disposed.</summary>
    public void Dispose()
    {
        if (!TryTransition(CompositeState.Disposed))
            return;

        Interlocked.Exchange(ref Content, null)?.Dispose();
    }
}