using Lyra.Common;
using Lyra.Imaging.Content;

namespace Lyra.Imaging.Decoding;

[Flags]
internal enum DecoderCapabilities
{
    None = 0,

    /// <summary>Decodes bytes another file holds, such as a texture inside a model, without any file of their own.</summary>
    ReadsFromMemory = 1 << 0,

    /// <summary>Shown only when opened itself, never as part of another file: camera raw, and models.</summary>
    OpensOnlyDirectly = 1 << 1
}

internal interface IImageDecoder
{
    /// <summary>The formats this decoder reads. No two decoders claim the same one.</summary>
    IReadOnlyList<ImageFormatType> Formats { get; }

    DecoderCapabilities Capabilities { get; }

    Task DecodeAsync(Composite composite, CancellationToken ct);
}
