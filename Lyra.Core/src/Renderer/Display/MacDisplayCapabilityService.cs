using Lyra.Common;
using Lyra.SystemUtils.MacInterop;
using static SDL3.SDL;

namespace Lyra.Renderer.Display;

/// <summary>
/// Reads EDR headroom from the <c>NSScreen</c> the window is currently on, and keeps reading it
/// as the window moves between displays.
/// </summary>
internal sealed class MacDisplayCapabilityService : IDisplayCapabilityService
{
    private readonly IntPtr _window;
    private readonly HeadroomTracker _tracker = new();

    private readonly IntPtr _selScreen    = ObjC.Sel("screen");
    private readonly IntPtr _selCurrent   = ObjC.Sel("maximumExtendedDynamicRangeColorComponentValue");
    private readonly IntPtr _selPotential = ObjC.Sel("maximumPotentialExtendedDynamicRangeColorComponentValue");
    private readonly IntPtr _selReference = ObjC.Sel("maximumReferenceExtendedDynamicRangeColorComponentValue");
    private readonly IntPtr _selNew       = ObjC.Sel("new");
    private readonly IntPtr _selDrain     = ObjC.Sel("drain");

    private uint _displayId;
    private string _displayName = "unknown";

    /// Set after an interop failure: one bad sample stops the asking rather than the renderer.
    private bool _degraded;

    public MacDisplayCapabilityService(IntPtr window)
    {
        if (window == IntPtr.Zero)
            throw new ArgumentException("A window is required to find the screen it is on.", nameof(window));

        _window = window;
    }

    public DisplayCapabilities Current => _tracker.Current;

    public event Action<DisplayCapabilities>? Changed;

    public void Poll()
    {
        if (_degraded)
            return;

        try
        {
            if (TrySample(out var sample) && _tracker.Observe(sample))
                Changed?.Invoke(sample);
        }
        catch (Exception ex)
        {
            _degraded = true;
            Logger.Warning($"[DisplayCapabilities] EDR headroom query failed, treating the display as SDR from here: {ex.Message}");
        }
    }

    private bool TrySample(out DisplayCapabilities sample)
    {
        sample = default;

        var screen = WindowScreen();
        if (screen == IntPtr.Zero)
            return false; // Minimised, or mid-move between displays: keep the last known answer.

        var displayId = GetDisplayForWindow(_window);
        if (displayId != _displayId)
        {
            _displayId = displayId;
            _displayName = GetDisplayName(displayId) is { Length: > 0 } name ? name : "unknown";
        }

        sample = DisplayCapabilities.Create(
            displayId,
            _displayName,
            Headroom(screen, _selPotential),
            Headroom(screen, _selCurrent),
            Headroom(screen, _selReference)
        );

        return true;
    }

    /// <summary>
    /// The NSScreen the window is on, or zero when it is on none.
    /// </summary>
    private IntPtr WindowScreen()
    {
        var props = GetWindowProperties(_window);
        var nsWindow = GetPointerProperty(props, Props.WindowCocoaWindowPointer, IntPtr.Zero);
        if (nsWindow == IntPtr.Zero)
            return IntPtr.Zero;

        var pool = ObjC.Send(ObjC.Class("NSAutoreleasePool"), _selNew);
        try
        {
            return ObjC.Send(nsWindow, _selScreen);
        }
        finally
        {
            if (pool != IntPtr.Zero)
                ObjC.SendVoid(pool, _selDrain);
        }
    }

    /// NaN for a selector this OS does not have; <see cref="DisplayCapabilities.Create"/> turns
    /// that into 1.0.
    private static double Headroom(IntPtr screen, IntPtr selector)
        => ObjC.Responds(screen, selector) ? ObjC.SendDouble(screen, selector) : double.NaN;
}