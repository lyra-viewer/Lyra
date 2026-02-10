using System.Collections.Concurrent;
using Lyra.SystemUtils;
using SkiaSharp;

namespace Lyra.Renderer.Overlay;

public static class FontHelper
{
    private static readonly Lazy<string> MonoFontPath = new(TtfLoader.GetMonospaceFontPath, isThreadSafe: true);
    private static readonly ConcurrentDictionary<string, Lazy<SKTypeface>> TypefaceCache = new();

    public static SKFont GetScaledMonoFont(float baseSize, float scale)
    {
        var path = MonoFontPath.Value;

        var lazyTf = TypefaceCache.GetOrAdd(
            path,
            p => new Lazy<SKTypeface>(() => SKTypeface.FromFile(p), isThreadSafe: true)
        );

        var tf = lazyTf.Value;
        return new SKFont(tf, baseSize * scale);
    }
}