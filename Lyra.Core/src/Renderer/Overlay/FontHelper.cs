using System.Collections.Concurrent;
using Lyra.SystemUtils;
using SkiaSharp;

namespace Lyra.Renderer.Overlay;

public static class FontHelper
{
    private static readonly Lazy<string> MonoFontPath = new(TtfLoader.GetMonospaceFontPath, isThreadSafe: true);
    private static readonly ConcurrentDictionary<string, Lazy<SKTypeface>> TypefaceCache = new();

    public static SKFont GetMonoFont(float size)
    {
        var path = MonoFontPath.Value;

        var lazyTf = TypefaceCache.GetOrAdd(
            path,
            p => new Lazy<SKTypeface>(() => SKTypeface.FromFile(p), isThreadSafe: true)
        );

        return new SKFont(lazyTf.Value, size);
    }
}