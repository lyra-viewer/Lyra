namespace Lyra.Metadata;

public interface IMetadataBlock<out TSelf> where TSelf : class, IMetadataBlock<TSelf>
{
    static abstract TSelf? Parse(ReadOnlySpan<byte> data);
}