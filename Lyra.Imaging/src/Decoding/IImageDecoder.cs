using Lyra.Common;
using Lyra.Imaging.Content;

namespace Lyra.Imaging.Decoding;

internal interface IImageDecoder
{
    bool CanDecode(ImageFormatType format);
    
    Task DecodeAsync(Composite composite, CancellationToken ct);
}
