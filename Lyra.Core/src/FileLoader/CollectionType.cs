namespace Lyra.FileLoader;

public enum CollectionType
{
    // A: one file "opened with...", or drag'n'dropped                                      -> anchor: that one file;           load other files around: true;      recursion: false;
    // B: one directory without any subdirectories "opened with...", or drag'n'dropped      -> anchor: first in collection;     load other files around: n/a;       recursion: false;
    SingleDirectoryCollection,
    
    // more than one file within the same directory                                         -> anchor: first in collection;     load other files around: false;     recursion: false 
    SingleDirectorySelection,
    
    // A: one directory with subdirectories "opened with...", or drag'n'dropped             -> anchor: first in collection;     load other files around: n/a;       recursion: true;
    // B: many directories "opened with...", or drag'n'dropped                              -> anchor: first in collection;     load other files around: n/a;       recursion: true;
    MultiDirectorySelection, 
    
    Undefined
}