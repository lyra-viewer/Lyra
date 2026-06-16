namespace Lyra.Common.Events;

public readonly record struct DrawableSizeChangedEvent(int PixelWidth, int PixelHeight, float Scale);
