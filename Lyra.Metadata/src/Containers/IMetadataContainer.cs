namespace Lyra.Metadata.Containers;

public interface IMetadataContainer
{
    static abstract bool Recognizes(ReadOnlySpan<byte> head);
}

public interface IBufferedContainer : IMetadataContainer
{
    static abstract ContainerContents? Read(ReadOnlySpan<byte> data);
}

public interface IStreamedContainer : IMetadataContainer
{ 
    static abstract ContainerContents? Read(Stream stream);
}