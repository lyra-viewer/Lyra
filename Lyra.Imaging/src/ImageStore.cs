using Lyra.Common.Estimation;
using Lyra.Imaging.ConstraintsProvider;
using Lyra.Imaging.Content;
using Lyra.Imaging.Decoding.Support;
using Lyra.Imaging.Loading;

namespace Lyra.Imaging;

public static class ImageStore
{
    private static readonly ImageLoader ImageLoader = new();

    public static void Initialize()
    {
        _ = DecodeConstraintsProvider.Current;
        ScratchFileCopy.SweepStaleFiles();
    }
    
    public static Composite GetImage(string path)
    {
        if (!IsLoading(path) && !File.Exists(path))
            throw new FileNotFoundException($"File not found: {path}");

        return ImageLoader.GetImage(path);
    }
    
    public static bool IsLoading(string path) => ImageLoader.IsLoading(path);

    public static void Preload(string[] paths)
    {
        ImageLoader.PreloadAdjacent(paths);
    }
    
    public static long ResidentBytes() => ImageLoader.ResidentBytes();
    
    public static long CacheBudgetBytes => ImageLoader.CacheBudgetBytes;

    public static void Cleanup(string[] keep)
    {
        ImageLoader.Cleanup(keep);
    }

    public static void Purge(string path)
    {
        ImageLoader.Purge(path);
    }

    public static void SaveAndDispose()
    {
        DecodeTimeEstimator.SaveTimeDataToFile();
        ImageLoader.Dispose();
    }
}