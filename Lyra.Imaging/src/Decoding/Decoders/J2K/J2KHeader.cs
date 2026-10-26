using Lyra.Imaging.Decoding.Support;
using Lyra.Metadata.Containers;

namespace Lyra.Imaging.Decoding.Decoders.J2K;

/// <summary>Refuses a JPEG 2000 image whose header declares an impossible size, before OpenJPEG allocates for it.</summary>
internal static class J2KHeader
{
    /// <summary>
    /// Refuses an impossible size from the header alone; OpenJPEG would allocate and decode all of
    /// it before the decoded dimensions could be checked.
    /// </summary>
    public static void RequireDeclaredSizeFits(ReadOnlySpan<byte> data)
    {
        if (Jp2Container.ReadSize(data) is not { } declared)
            return;

        DecoderValidation.RequireSaneDimensions(ClampToInt(declared.Width), ClampToInt(declared.Height));

        // OpenJPEG holds every component as a 32-bit int, beside the RGBA it is converted to.
        DecoderValidation.RequireAvailableMemory(declared.Width, declared.Height, 4 + 4 * Math.Max(1, declared.Components));
    }

    private static int ClampToInt(long value) => (int)Math.Min(value, int.MaxValue);
}
