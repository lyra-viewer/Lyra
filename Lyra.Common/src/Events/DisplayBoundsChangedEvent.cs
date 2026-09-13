namespace Lyra.Common.Events;

/// <summary>
/// The size of the display the window is on. A state, not an occurrence - retained, so that
/// whatever reads it can subscribe whenever it likes and still learn where the window is.
/// </summary>
public readonly record struct DisplayBoundsChangedEvent(int PixelWidth, int PixelHeight, uint? DisplayId = null)
    : IRetainedEvent;
