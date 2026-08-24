namespace Lyra.Renderer.Display;

public sealed class SdrDisplayCapabilityService : IDisplayCapabilityService
{
    public DisplayCapabilities Current => DisplayCapabilities.Sdr;

    /// Never raised: an SDR display has nothing to report a change in.
    public event Action<DisplayCapabilities>? Changed
    {
        add { }
        remove { }
    }

    public void Poll() { }
}