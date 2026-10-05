using static Lyra.Imaging.Interop.HeifNative;

namespace Lyra.Imaging.Interop;

/// <summary>
/// A HEIF or AVIF file opened by libheif, which reads it through the stream on demand: the
/// stream must stay open, and positioned reads on it allowed, until this is disposed.
/// </summary>
internal sealed class HeifFile : IDisposable
{
    private IntPtr _context;
    private readonly HeifStreamReader _reader;

    private HeifFile(IntPtr context, HeifStreamReader reader)
    {
        _context = context;
        _reader = reader;
    }

    public static HeifFile Open(Stream stream, CancellationToken ct = default)
    {
        var context = heif_context_alloc();
        if (context == IntPtr.Zero)
            throw new OutOfMemoryException("libheif could not allocate a context.");

        var file = new HeifFile(context, new HeifStreamReader(stream) { Cancellation = ct });

        try
        {
            file.Check(heif_context_read_from_reader(context, file._reader.Native, file._reader.UserData, IntPtr.Zero), "Could not read the file");
            return file;
        }
        catch
        {
            file.Dispose();
            throw;
        }
    }

    public IntPtr Context => _context;

    public CancellationToken Cancellation
    {
        set => _reader.Cancellation = value;
    }

    public bool ReadsCancelled => _reader.Cancelled;

    public int TopLevelImageCount => heif_context_get_number_of_top_level_images(_context);

    public bool HasPrimaryImage => !heif_context_get_primary_image_ID(_context, out _).Failed;

    public HeifImageHandle PrimaryImage()
    {
        Check(heif_context_get_primary_image_handle(_context, out var handle), "No primary image");
        return new HeifImageHandle(handle, this);
    }

    public bool HasSequence => heif_context_has_sequence(_context) != 0;

    public HeifTrack? Track(uint id)
    {
        var track = heif_context_get_track(_context, id);
        return track == IntPtr.Zero ? null : new HeifTrack(track, this);
    }

    public void Check(HeifError error, string what)
    {
        if (!error.Failed)
            return;

        if (_reader.Cancelled)
            throw new OperationCanceledException("Reading the file was cancelled.", _reader.Cancellation);

        error.ThrowIfFailed(what);
    }

    public void Dispose()
    {
        if (_context != IntPtr.Zero)
        {
            heif_context_free(_context);
            _context = IntPtr.Zero;
        }

        _reader.Dispose();
    }
}