using SkiaSharp;
using static Lyra.Imaging.Interop.HeifNative;

namespace Lyra.Imaging.Interop;

/// <summary>
/// A track of a HEIF or AVIF image sequence. libheif decodes it forward only, one sample after
/// the next, from a read position kept by the file: going back means opening the file again.
/// </summary>
internal sealed class HeifTrack(IntPtr track, HeifFile file) : IDisposable
{
    private static bool _repetitionsEntryPointMissing;

    private IntPtr _track = track;

    /// <summary>The size of the pictures, which coded frames can exceed. Null when libheif cannot tell.</summary>
    public SKSizeI? Size => heif_track_get_image_resolution(_track, out var width, out var height).Failed ? null : new SKSizeI(width, height);

    /// <summary>Whether libheif merges alpha into the frames itself.</summary>
    public bool HasAlphaChannel => heif_track_has_alpha_channel(_track) != 0;

    /// <summary>How many times the track plays in all; null when the file does not say, or libheif cannot tell.</summary>
    public uint? Repetitions
    {
        get
        {
            if (Volatile.Read(ref _repetitionsEntryPointMissing))
                return null;

            try
            {
                var repetitions = heif_track_get_number_of_repetitions(_track);
                return repetitions == 0 ? null : repetitions;
            }
            catch (EntryPointNotFoundException)
            {
                Volatile.Write(ref _repetitionsEntryPointMissing, true);
                return null;
            }
        }
    }
    
    public HeifImage? DecodeNextRgba() => DecodeNext(ColorspaceRgb, ChromaInterleavedRgba);
    
    public HeifImage? DecodeNextAlpha() => DecodeNext(ColorspaceMonochrome, ChromaMonochrome);

    private HeifImage? DecodeNext(int colorspace, int chroma)
    {
        var error = heif_track_decode_next_image(_track, out var image, colorspace, chroma, IntPtr.Zero);
        if (error.Code == ErrorEndOfSequence)
            return null;

        file.Check(error, "Could not decode the frame");
        return new HeifImage(image);
    }

    public void Dispose()
    {
        if (_track == IntPtr.Zero)
            return;

        heif_track_release(_track);
        _track = IntPtr.Zero;
    }
}