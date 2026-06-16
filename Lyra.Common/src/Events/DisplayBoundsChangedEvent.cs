namespace Lyra.Common.Events;

public readonly record struct DisplayBoundsChangedEvent(int PixelWidth, int PixelHeight, uint? DisplayId = null);
