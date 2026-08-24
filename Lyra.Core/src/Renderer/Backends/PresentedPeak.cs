using System.Buffers.Binary;
using Lyra.Common;
using SkiaSharp;

namespace Lyra.Renderer.Backends;

/// <summary>
/// Reports the brightest value a rendered frame actually handed to the display, in multiples of
/// SDR white.
/// </summary>
/// <remarks>
/// The only way to ask whether EDR is live from inside the app: extended range does not survive a
/// screen capture, so an EDR window and an SDR one produce identical screenshots. A peak of 1.0
/// places the fault upstream - no headroom granted, or content baked at decode with no highlights
/// left to spend; above 1.0 the frame carried extended values and anything wrong lies between the
/// drawable and the panel.
///
/// Costs a full-surface read, so it runs once per session.
/// </remarks>
internal sealed class PresentedPeak
{
    /// Frames to let pass *after* an image is on screen, so the reading is of a drawn picture.
    private const int FramesAfterContent = 90;

    private int _frame;
    private bool _done;

    /// <summary>
    /// Samples once, some frames after there is something drawn: counting from startup would
    /// report an empty canvas for any image that takes seconds to decode.
    /// </summary>
    public void Observe(SKSurface surface, string backend, bool hasContent)
    {
        if (_done || !hasContent || ++_frame < FramesAfterContent)
            return;

        _done = true;

        try
        {
            Report(surface, backend);
        }
        catch (Exception ex)
        {
            // A diagnostic must never be the reason a frame fails.
            Logger.Debug($"[{backend}] Could not sample the presented peak: {ex.Message}");
        }
    }

    private static void Report(SKSurface surface, string backend)
    {
        using var snapshot = surface.Snapshot();

        // Linear half-float, so the numbers are light rather than encoded values.
        var info = new SKImageInfo(snapshot.Width, snapshot.Height, SKColorType.RgbaF16, SKAlphaType.Unpremul, SKColorSpace.CreateSrgbLinear());

        using var pixels = new SKBitmap(info);
        if (!snapshot.ReadPixels(pixels.Info, pixels.GetPixels(), pixels.RowBytes, 0, 0))
        {
            Logger.Debug($"[{backend}] Presented peak unavailable: the surface would not read back.");
            return;
        }

        var peak = Peak(pixels);

        Logger.Info($"[{backend}] Peak value presented: {peak:0.###}x SDR white " +
                    (peak > 1.001f
                        ? "- the frame is carrying extended range."
                        : "- nothing above SDR white was drawn."));
    }

    /// <summary>The largest channel value anywhere in the frame.</summary>
    private static float Peak(SKBitmap pixels)
    {
        var span = pixels.GetPixelSpan();
        var peak = 0f;

        // Half-float RGBA: four channels of two bytes. Alpha is skipped - it is not light.
        for (var offset = 0; offset + 8 <= span.Length; offset += 8)
        for (var channel = 0; channel < 3; channel++)
        {
            var value = (float)BinaryPrimitives.ReadHalfLittleEndian(span.Slice(offset + (channel * 2), 2));
            if (value > peak && float.IsFinite(value))
                peak = value;
        }

        return peak;
    }
}
