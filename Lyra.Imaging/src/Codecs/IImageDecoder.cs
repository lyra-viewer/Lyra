using Lyra.Common;
using Lyra.Imaging.Content;

namespace Lyra.Imaging.Codecs;

internal interface IImageDecoder
{
    bool CanDecode(ImageFormatType format);
    
    Task DecodeAsync(Composite composite, CancellationToken ct);
}
