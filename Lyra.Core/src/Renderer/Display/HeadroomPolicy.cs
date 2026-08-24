using Lyra.Renderer.Drawing;

namespace Lyra.Renderer.Display;

internal static class HeadroomPolicy
{
    private const float Bootstrap = 2f;

    public static float Spendable(DisplayCapabilities display)
    {
        if (!display.SupportsExtendedRange)
            return SurfaceProfile.SdrWhite;

        return MathF.Max(display.CurrentHeadroom, MathF.Min(display.PotentialHeadroom, Bootstrap));
    }
}