using Lyra.FileLoader.Enumeration;
using Lyra.SdlCore;

namespace Lyra.Renderer;

public readonly record struct ApplicationStates(
    CollectionType CollectionType,
    int CollectionIndex,
    int CollectionCount,
    int? DirectoryIndex,
    int? DirectoryCount,

    bool InDuplicatesMode,

    int Zoom,
    DisplayMode DisplayMode,
    string SamplingMode,
    
    bool InfoVisible,
    bool HelpVisible,
    bool SidebarVisible,
    
    bool DropActive,
    bool DropAborted,
    long DropPathsEnqueued,
    long DropFilesEnumerated,
    long DropFilesSupported,
    
    string Backend
);