namespace Lyra.Renderer.Display;

/// <summary>
/// Tracks the EDR capability of the display the window is on, and follows the window when it
/// moves to another one.
/// </summary>
public interface IDisplayCapabilityService
{
    DisplayCapabilities Current { get; }

    /// Raised for a display change, a change in whether extended range is possible, or a headroom
    /// move worth reporting - never for every sample.
    event Action<DisplayCapabilities>? Changed;

    /// Takes one sample; call once per frame. Costs a handful of objc_msgSend calls.
    void Poll();
}