using Lyra.Imaging.Content;
using Lyra.Imaging.Decoding.Support;
using Lyra.Imaging.Interop;
using SkiaSharp;

namespace Lyra.Imaging.Decoding.Decoders.Animation;

/// <summary>
/// Frames of a HEIF or AVIF image sequence. libheif only decodes a track forward, one sample
/// after the next, so the context stays positioned after the last frame decoded: the next one
/// costs one decode, and going back means opening the file again and decoding up to it.
/// </summary>
internal sealed class HeifSequenceFrameSource(Func<Stream> open, uint colorTrackId, uint? alphaTrackId, int count, SKSizeI size, SKColorSpace? colorSpace, Composite composite) : ExclusiveResource, IFrameSource
{
    private Stream? _stream;
    private HeifFile? _file;
    private HeifTrack? _color;
    private HeifTrack? _alpha;
    private int _next;

    public int Count => count;

    public ICompositeContent Decode(int index, CancellationToken ct) => Exclusive(() => DecodeLocked(index, ct));

    private ICompositeContent DecodeLocked(int index, CancellationToken ct)
    {
        if (index < 0 || index >= count)
            throw new ArgumentOutOfRangeException(nameof(index));

        try
        {
            if (_file is null || index < _next)
                Open(ct);

            _file!.Cancellation = ct;

            while (_next < index)
            {
                ct.ThrowIfCancellationRequested();
                Next().Dispose();
            }

            ct.ThrowIfCancellationRequested();

            using var frame = Next();
            return RasterContentBuilder.Build(Bitmap(frame, index, ct), composite);
        }
        catch (OperationCanceledException) when (_file is null || _file.ReadsCancelled)
        {
            // A read refused mid-open or mid-decode leaves nothing to continue from.
            Release();
            throw;
        }
    }

    private void Open(CancellationToken ct)
    {
        Release();

        _stream = open();

        try
        {
            _file = HeifFile.Open(_stream, ct);
        }
        catch (HeifException e)
        {
            throw Failure($"The file can no longer be read: {e.Message}");
        }

        _color = _file.Track(colorTrackId) ?? throw Failure($"The file no longer has track {colorTrackId}");

        if (alphaTrackId is { } id)
            _alpha = _file.Track(id) ?? throw Failure($"The file no longer has its alpha track ({id})");

        _next = 0;
    }

    private HeifFrame Next()
    {
        var number = _next + 1;
        HeifFrame? frame;

        try
        {
            frame = HeifFrame.DecodeNext(_color!, _alpha);
        }
        catch (HeifException e)
        {
            throw Failure($"Frame {number}: {e.Message}");
        }

        _next++;
        return frame ?? throw Failure($"The sequence ends before frame {number}");
    }

    private SKBitmap Bitmap(HeifFrame frame, int index, CancellationToken ct)
    {
        try
        {
            return frame.ToBitmap(size, colorSpace, ct);
        }
        catch (HeifException e)
        {
            throw Failure($"Frame {index + 1}: {e.Message}");
        }
    }

    private LoadFailureException Failure(string reason)
    {
        Release();
        return new LoadFailureException(LoadFailureKind.DecodeFailed, reason);
    }

    protected override void Release()
    {
        _alpha?.Dispose();
        _color?.Dispose();
        _alpha = _color = null;

        _file?.Dispose();
        _file = null;

        _stream?.Dispose();
        _stream = null;

        _next = 0;
    }
}