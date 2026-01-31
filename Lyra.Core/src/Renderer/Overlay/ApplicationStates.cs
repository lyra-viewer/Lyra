namespace Lyra.Renderer.Overlay;

public readonly record struct ApplicationStates(
    string CollectionType,
    int CollectionIndex,
    int CollectionCount,
    int? DirectoryIndex,
    int? DirectoryCount,
    
    int Zoom,
    string DisplayMode,
    string SamplingMode,
    
    bool ShowExif,
    
    bool DropActive,
    bool DropAborted,
    long DropPathsEnqueued,
    long DropFilesEnumerated,
    long DropFilesSupported
);