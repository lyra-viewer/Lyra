using System.Reflection;
using SkiaSharp;
using Svg.Skia;

namespace Lyra.UI;

// ============================================================================
//  ResourceLoader - cached SVG picture loader for embedded icon resources.
// ----------------------------------------------------------------------------
//  Centralizes all access to Resources/Icons/*.svg embedded in the LyraUI
//  assembly. Callers receive a cached SKPicture and wrap it in their own
//  SvgImage instance, which independently manages layout state and lifetime.
//
//  Ownership contract:
//    - ResourceLoader owns and never disposes cached SKPictures.
//    - SvgImage(SKPicture, ...) does not dispose the picture it wraps.
//    - Callers own and dispose the SvgImage instances they create.
// ============================================================================
public static class ResourceLoader
{
    private static readonly Assembly Assembly = typeof(ResourceLoader).Assembly;
    private static readonly Dictionary<string, SKPicture> Cache = new();

    /// Returns a cached SKPicture for the named icon.
    /// Do not dispose the returned instance - it is owned by ResourceLoader.
    /// Wrap it in a new SvgImage for each use site.
    public static SKPicture GetSvg(string name)
    {
        if (Cache.TryGetValue(name, out var cached))
            return cached;

        var manifestName = Assembly.GetName().Name + ".Resources.Icons." + name + ".svg";
        using var stream = Assembly.GetManifestResourceStream(manifestName)
                           ?? throw new InvalidOperationException($"SVG resource not found: '{name}'.");

        var svg = new SKSvg();
        var picture = svg.Load(stream) 
                      ?? throw new InvalidOperationException($"Failed to parse SVG resource: '{name}'.");

        Cache[name] = picture;
        return picture;
    }
}