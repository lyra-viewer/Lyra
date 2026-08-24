using Lyra.Common;

namespace Lyra.Renderer.Display;

/// <summary>
/// Builds the display capability service for this platform.
/// </summary>
public static class DisplayCapabilityService
{
    public static IDisplayCapabilityService None { get; } = new SdrDisplayCapabilityService();
    
    public static IDisplayCapabilityService Create(IntPtr window)
    {
        if (!OperatingSystem.IsMacOS() || window == IntPtr.Zero)
            return None;

        try
        {
            return new MacDisplayCapabilityService(window);
        }
        catch (Exception ex)
        {
            Logger.Warning($"[DisplayCapabilityService] Could not start the macOS EDR query, continuing as SDR: {ex.Message}");
            return None;
        }
    }
}
