using Lyra.Imaging.Data;
using Lyra.Imaging.Pipeline;
using Lyra.Imaging.Psd.ImageSharp;
using SixLabors.ImageSharp;

namespace Lyra.Imaging;

public static class ImageStore
{
    private static readonly ImageLoader ImageLoader = new();
    
    public static void Initialize()
    {
        NativeLibraryLoader.Initialize();
        
        var formatsManager = Configuration.Default.ImageFormatsManager;
        formatsManager.AddImageFormat(PsdFormat.Instance);
        formatsManager.AddImageFormatDetector(new PsdFormatDetector());
        formatsManager.SetDecoder(PsdFormat.Instance, new PsdDecoder());
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