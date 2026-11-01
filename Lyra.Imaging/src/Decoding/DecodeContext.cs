using Lyra.Common;
using Lyra.Imaging.Content;
using Lyra.Imaging.Content.Exif;
using Lyra.Imaging.Content.Status;
using Lyra.Psd.Core.Decode.Layers;

namespace Lyra.Imaging.Decoding;

internal enum BackgroundSchedule
{
    /// <summary>With the load, so it waits behind the image on screen and can be moved ahead when it becomes it.</summary>
    WithLoad,

    /// <summary>On the thread pool, for work that holds its thread from start to end and would block the load's workers.</summary>
    ThreadPool
}

/// <summary>
/// What a decoder sees of a load: the bytes it reads, the settings it runs under, and the parts of
/// the composite it fills in. The composite's state, events and lifetime stay with the loader.
/// </summary>
internal sealed class DecodeContext
{
    private readonly Composite _composite;

    public DecodeContext(Composite composite, DecodeSource source, DecodeOptions options)
    {
        _composite = composite;
        Source = source;
        Options = options;
    }

    public static DecodeContext For(Composite composite) => new(composite, SourceOf(composite), DecodeOptions.Current());

    private static DecodeSource SourceOf(Composite composite) =>
        composite.Payload is { } payload
            ? DecodeSource.Memory(composite.FileInfo.FullName, payload)
            : DecodeSource.File(composite.FileInfo.FullName, composite.FileSizeBytes, composite.Head);

    public DecodeSource Source { get; }

    public DecodeOptions Options { get; }

    public ImageFormatType ImageFormatType => _composite.ImageFormatType;

    public string? DecoderName
    {
        get => _composite.DecoderName;
        set => _composite.DecoderName = value;
    }

    /// <summary>Whether the image is the one on screen, so its work goes ahead of preloads.</summary>
    public bool IsCurrent => _composite.IsCurrent;

    #region Reading

    public byte[] ReadAllBytes(CancellationToken ct)
    {
        var data = Source.ReadAllBytes(ct, out var elapsedMs, _composite.ReportTransferred);
        _composite.CompleteTransfer(data.Length, elapsedMs);
        return data;
    }

    public Stream OpenSequentialRead() => Source.OpenSequential();

    public void ReportTransferred(long bytesSoFar) => _composite.ReportTransferred(bytesSoFar);

    public void CompleteTransfer(long bytes, double ms) => _composite.CompleteTransfer(bytes, ms);

    #endregion

    #region What the decoder fills in

    public ICompositeContent? Content
    {
        get => _composite.Content;
        set
        {
            (value as RasterLargeContent)?.Seal();
            _composite.Content = value;
        }
    }

    public ExifInfo? ExifInfo
    {
        get => _composite.ExifInfo;
        set => _composite.ExifInfo = value;
    }

    /// <summary>Sets the metadata unless another reader got there first.</summary>
    public void SetExifInfoIfAbsent(ExifInfo exifInfo) => Interlocked.CompareExchange(ref _composite.ExifInfo, exifInfo, null);

    public ExifOrientation AppliedOrientation
    {
        get => _composite.AppliedOrientation;
        set => _composite.AppliedOrientation = value;
    }

    public LoadWarning? Warning
    {
        get => _composite.Warning;
        set => _composite.Warning = value;
    }

    public LoadStage? Stage
    {
        get => _composite.Stage;
        set => _composite.Stage = value;
    }

    public bool HdrPending
    {
        get => _composite.HdrPending;
        set => _composite.HdrPending = value;
    }

    public string? HdrBakedReason
    {
        get => _composite.HdrBakedReason;
        set => _composite.HdrBakedReason = value;
    }

    public IReadOnlyList<StructureGroup>? Structure
    {
        get => _composite.Structure;
        set => _composite.Structure = value;
    }

    public LayerRecord[]? PsdLayers
    {
        get => _composite.PsdLayers;
        set => _composite.PsdLayers = value;
    }

    public void SetFullSize(float width, float height) => _composite.SetFullSize(width, height);

    public void AddFormatSpecific(string key, string value) => _composite.AddFormatSpecific(key, value);

    public void AddFormatSpecific(string key, bool value) => _composite.AddFormatSpecific(key, value);

    #endregion

    #region Progress

    public void ReportPixelCount(long width, long height) => _composite.ReportPixelCount(width, height);

    public void SignalReady() => _composite.SignalReady();

    private void SignalComplete() => _composite.SignalComplete();

    public void SignalProgress() => _composite.SignalProgress();

    private void MarkCancelled() => _composite.TryTransition(CompositeState.Cancelled);

    #endregion

    #region Work after the decode returns

    private int _continued;

    /// <summary>
    /// Goes on with <paramref name="work"/> after the decode returns, such as filling in tiles or
    /// developing behind a preview. The loader keeps the composite until it has finished.
    /// Completion is signaled when it does. A cancellation marks the load canceled, and a failure
    /// leaves what was shown with a warning. Once per decode.
    /// </summary>
    public void ContinueInBackground(Func<CancellationToken, Task> work, BackgroundSchedule schedule, CancellationToken ct)
    {
        if (Interlocked.Exchange(ref _continued, 1) != 0)
            throw new InvalidOperationException("A decode continues in the background only once.");

        var scheduler = schedule == BackgroundSchedule.WithLoad ? TaskScheduler.Current : TaskScheduler.Default;

        Task<Task> start;
        try
        {
            start = Task.Factory.StartNew(() => ContinueAsync(work, ct), CancellationToken.None, TaskCreationOptions.DenyChildAttach, scheduler);
        }
        catch (Exception e) when (e is InvalidOperationException or TaskSchedulerException)
        {
            // The scheduler is shutting down; the work runs here instead.
            ContinueAsync(work, ct).GetAwaiter().GetResult();
            return;
        }

        _composite.BackgroundDecodeStart = start;
        _composite.BackgroundDecodeTask = start.Unwrap();
    }
    
    private async Task ContinueAsync(Func<CancellationToken, Task> work, CancellationToken ct)
    {
        try
        {
            await work(ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            MarkCancelled();
            return;
        }
        catch (Exception e)
        {
            var failure = LoadFailure.From(e, Source.Label);

            if (failure.IsExpected)
                Logger.Warning($"[{DecoderName}] {Source.Label} was left unfinished: {failure.Description}");
            else
                Logger.Error($"[{DecoderName}] {Source.Label} was left unfinished: {e}");

            Warning ??= LoadWarning.PartiallyDecoded(failure.Description);
        }

        SignalComplete();
    }

    #endregion
}