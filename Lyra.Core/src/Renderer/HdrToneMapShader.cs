using Lyra.Common;
using Lyra.Common.Settings.Enums;
using SkiaSharp;

namespace Lyra.Renderer;

/// <summary>
/// Draw-time tone mapping for scene-referred HDR images.
///
/// If the effect fails to compile - an old driver, a backend without runtime effects - the
/// property is null and the caller draws the image unmapped rather than not at all.
/// </summary>
internal static class HdrToneMapShader
{
    private const string Sksl = """
        uniform shader image;
        uniform float exposure;
        uniform float whitePoint;
        uniform float mode;

        half4 main(float2 coord) {
            half4 src = image.eval(coord);
            float3 x = max(float3(src.rgb) * exposure, float3(0.0));

            float3 mapped;
            if (mode > 1.5) {
                // Clip
                mapped = x;
            } else if (mode > 0.5) {
                // Reinhard extended
                float w2 = whitePoint * whitePoint;
                mapped = x * (float3(1.0) + x / w2) / (float3(1.0) + x);
            } else {
                // ACES filmic (Narkowicz 2015)
                mapped = (x * (2.51 * x + 0.03)) / (x * (2.43 * x + 0.59) + 0.14);
            }

            mapped = clamp(mapped, float3(0.0), float3(1.0));
            mapped = pow(mapped, float3(1.0 / 2.2));

            return half4(half3(mapped), src.a);
        }
        """;

    private static readonly Lazy<SKRuntimeEffect?> Effect = new(Compile);

    private static SKRuntimeEffect? Compile()
    {
        try
        {
            var effect = SKRuntimeEffect.CreateShader(Sksl, out var errors);
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
    
    public static SKPaint? CreatePaint(SKImage image, SKSamplingOptions sampling, SKMatrix localMatrix, ToneMapMode mode, float exposureScale, float whitePoint)
    {
        var effect = Effect.Value;
        if (effect is null)
            return null;

        // The image is linear half-float; sample it without any color conversion, since the
        // shader is what decides how these values become display values.
        using var imageShader = image.ToShader(SKShaderTileMode.Clamp, SKShaderTileMode.Clamp, sampling, localMatrix);

        var uniforms = new SKRuntimeEffectUniforms(effect)
        {
            ["exposure"] = exposureScale,
            ["whitePoint"] = MathF.Max(whitePoint, 1e-3f),
            ["mode"] = (float)mode
        };

        var children = new SKRuntimeEffectChildren(effect) { ["image"] = imageShader };

        var shader = effect.ToShader(uniforms, children);

        return new SKPaint { Shader = shader, IsAntialias = false };
    }

    /// <summary>Whether draw-time tone mapping is usable at all on this machine.</summary>
    public static bool IsAvailable => Effect.Value is not null;
}
