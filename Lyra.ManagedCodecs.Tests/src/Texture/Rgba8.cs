using Lyra.ManagedCodecs.Texture;

namespace Lyra.ManagedCodecs.Tests.Texture;

internal static class Rgba8
{
    public static byte[] BufferFor(in Subresource sr) => new byte[checked((long)sr.Width * sr.Height * 4)];
}