using Lyra.Common;
using Lyra.Common.Settings.Enums;
using SkiaSharp;

namespace Lyra.Renderer.Drawing;

/// <summary>
/// Draw-time tone mapping for scene-referred HDR images.
///
/// If the effect fails to compile - an old driver, a backend without runtime effects - the
/// property is null and the caller draws the image unmapped rather than not at all.
/// </summary>
internal static class HdrToneMapShader
{
    private const string ShaderResourceName = "LyraViewer.Shaders.HdrToneMap.sksl";


    private static readonly Lazy<SKRuntimeEffect?> Effect = new(Compile);
    
    private static string? ReadSource()
    {
        using var stream = typeof(HdrToneMapShader).Assembly.GetManifestResourceStream(ShaderResourceName);
        if (stream is null)
        {
            Logger.Error($"[HdrToneMapShader] Shader resource {ShaderResourceName} is missing from the assembly; HDR images will draw without tone mapping.");
            return null;
        }

        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private static SKRuntimeEffect? Compile()
    {
        try
        {
            var source = ReadSource();
            if (source is null)
                return null;

            var effect = SKRuntimeEffect.CreateShader(source, out var errors);
            if (effect is null || !string.IsNullOrEmpty(errors))
            {
                Logger.Error($"[HdrToneMapShader] Runtime effect failed to compile: {errors}");
                return null;
            }

            return effect;
        }
        catch (Exception ex)
        {
            Logger.Error($"[HdrToneMapShader] Runtime effects unavailable: {ex.Message}");
            return null;
        }
    }
    
    public static SKPaint? CreatePaint(SKImage image, SKSamplingOptions sampling, SKMatrix localMatrix, ToneMapMode mode, float exposureScale, float whitePoint, SurfaceProfile surface)
    {
        var effect = Effect.Value;
        if (effect is null)
            return null;
        
        using var imageShader = image.ToRawShader(SKShaderTileMode.Clamp, SKShaderTileMode.Clamp, sampling, localMatrix);

        var transfer = TransferOf(surface.ColorSpace);

        var uniforms = new SKRuntimeEffectUniforms(effect)
        {
            ["exposure"]    = exposureScale,
            ["whitePoint"]  = MathF.Max(whitePoint, 1e-3f),
            ["mode"]        = (float)mode,
            ["gamut"]       = GamutTransform.Between(image.ColorSpace, surface.ColorSpace),
            ["ceiling"]     = surface.Ceiling,
            ["lumaWeights"] = GamutTransform.LuminanceWeights(surface.ColorSpace),
            ["encodeGABC"]  = new[] { transfer.G, transfer.A, transfer.B, transfer.C },
            ["encodeDEF"]   = new[] { transfer.D, transfer.E, transfer.F }
        };

        var children = new SKRuntimeEffectChildren(effect) { ["image"] = imageShader };
        using var shader = effect.ToShader(uniforms, children);

        return new SKPaint { Shader = shader, IsAntialias = false };
    }

    /// <summary>
    /// The transfer function of the surface being drawn into, which the shader has to apply itself
    /// because it sampled raw.
    /// </summary>
    private static SKColorSpaceTransferFn TransferOf(SKColorSpace? destination)
    {
        if (destination is not null && destination.GetNumericalTransferFunction(out var transfer))
            return transfer;

        return SKColorSpaceTransferFn.Srgb;
    }

    /// <summary>Whether draw-time tone mapping is usable at all on this machine.</summary>
    public static bool IsAvailable => Effect.Value is not null;
}
