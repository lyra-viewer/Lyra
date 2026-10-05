using Lyra.Imaging.Content;

namespace Lyra.Imaging.Decoding.Decoders.Animation;

/// <summary>Decodes any frame of an animation as it looks when shown. Safe to call from any thread.</summary>
internal interface IFrameSource : IDisposable
{
    int Count { get; }

    ICompositeContent Decode(int index, CancellationToken ct);
}