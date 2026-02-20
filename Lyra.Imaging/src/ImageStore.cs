using Lyra.Common;
using Lyra.Imaging.ConstraintsProvider;
using Lyra.Imaging.Content;
using Lyra.Imaging.Pipeline;

namespace Lyra.Imaging;

public static class ImageStore
{
    private static readonly ImageLoader ImageLoader = new();

    public static void Initialize()
    {
        _ = DecodeConstraintsProvider.Current;
    }
    
    public static Composite GetImage(string path)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException($"[ImageStore] File not found: {path}");

        return ImageLoader.GetImage(path);
    }

    public static void Preload(string[] paths)
    {
        ImageLoader.PreloadAdjacent(paths);
    }

    public static void Cleanup(string[] keep)
    {
        ImageLoader.Cleanup(keep);
    }

    public static void SaveAndDispose()
    {
        LoadTimeEstimator.SaveTimeDataToFile();
        ImageLoader.DisposeAll();
    }
}