namespace Lyra.Imaging.Psd.Core.Decode.Composite;

public readonly record struct TileInfo(
    int X, int Y,               // tile origin in image pixels
    int Width, int Height,      // tile size (edge tiles smaller)
    int Index                   // linear index
);